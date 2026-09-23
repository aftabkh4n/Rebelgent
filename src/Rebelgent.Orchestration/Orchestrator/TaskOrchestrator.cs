using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Agents;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Process;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.Orchestration.Orchestrator;

/// <summary>
/// Drives the full pipeline: worktree creation → Claude Code → build → test → persist result.
/// Registered as a singleton; uses IServiceScopeFactory for all scoped DB access.
/// </summary>
public sealed class TaskOrchestrator : ITaskOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProjectRegistry _projectRegistry;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ICodingAgentRunner _agentRunner;
    private readonly IProcessRunner _processRunner;
    private readonly ExecutionConcurrencyGuard _concurrencyGuard;
    private readonly IOptions<ExecutionOptions> _executionOptions;
    private readonly ILogger<TaskOrchestrator> _logger;

    public TaskOrchestrator(
        IServiceScopeFactory scopeFactory,
        IProjectRegistry projectRegistry,
        IWorkspaceManager workspaceManager,
        ICodingAgentRunner agentRunner,
        IProcessRunner processRunner,
        ExecutionConcurrencyGuard concurrencyGuard,
        IOptions<ExecutionOptions> executionOptions,
        ILogger<TaskOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _projectRegistry = projectRegistry;
        _workspaceManager = workspaceManager;
        _agentRunner = agentRunner;
        _processRunner = processRunner;
        _concurrencyGuard = concurrencyGuard;
        _executionOptions = executionOptions;
        _logger = logger;
    }

    public async Task<OrchestrationResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        // Acquire concurrency slot — only one execution at a time
        var acquired = await _concurrencyGuard.WaitAsync(TimeSpan.Zero, cancellationToken);
        if (!acquired)
        {
            return new OrchestrationResult
            {
                Succeeded = false,
                Summary = "An execution is already in progress. Please try again shortly.",
                ErrorMessage = "Concurrency limit reached."
            };
        }

        try
        {
            return await ExecuteInternalAsync(taskId, cancellationToken, false, null);
        }
        finally
        {
            _concurrencyGuard.Release();
        }
    }

    public async Task<OrchestrationResult> RetryAsync(Guid taskId, HumanPrincipal human, CancellationToken cancellationToken = default)
    {
        if (human is null)
            return new OrchestrationResult { Succeeded = false, Summary = "Retry requires authenticated human authority.", ErrorMessage = "Retry authorization denied." };

        var acquired = false;
        var executionStarted = false;
        try
        {
            using var readScope = _scopeFactory.CreateScope();
            var taskService = readScope.ServiceProvider.GetRequiredService<ITaskService>();
            var executionRepository = readScope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
            var releaseRepository = readScope.ServiceProvider.GetRequiredService<IReleaseRepository>();
            var packageRepository = readScope.ServiceProvider.GetRequiredService<IPackageRepository>();
            var authorization = readScope.ServiceProvider.GetRequiredService<IHumanAuthorizationService>();
            authorization.RequireHuman(human, HumanCapability.ApproveTask, "RetryTask", taskId);
            await RecordRetryAsync(AuditEventType.TaskRetryRequested, taskId, human, cancellationToken);

            acquired = await _concurrencyGuard.WaitAsync(TimeSpan.Zero, cancellationToken);
            if (!acquired)
                throw new InvalidOperationException("An execution is already in progress. Please try again shortly.");

            var task = await taskService.GetTaskAsync(taskId, cancellationToken);
            if (task is null)
                throw new InvalidOperationException("Task not found.");
            if (task.Status is not (AgentTaskStatus.Failed or AgentTaskStatus.Planning))
                throw new InvalidOperationException("Only failed tasks can be retried.");

            var project = _projectRegistry.Find(task.ProjectId);
            if (project is null)
                throw new InvalidOperationException($"Project '{task.ProjectId}' is not registered.");
            if (task.PullRequestNumber is not null || task.PullRequestUrl is not null || task.PullRequestCreatedAt is not null || task.MergedAt is not null || task.MergeCommitSha is not null || task.MergeMethod is not null)
                throw new InvalidOperationException("Tasks with pull requests or merges cannot be retried.");
            if (await releaseRepository.GetByTaskIdAsync(taskId, cancellationToken) is not null
                || await packageRepository.GetByTaskIdAsync(taskId, cancellationToken) is not null)
                throw new InvalidOperationException("Released or packaged tasks cannot be retried.");

            var executions = await executionRepository.GetAllByTaskIdAsync(taskId, cancellationToken);
            if (executions.Any(e => e.Status is ExecutionStatus.Queued or ExecutionStatus.Running))
                throw new InvalidOperationException("An execution is already active for this task.");
            if (executions.Any(e => !string.IsNullOrWhiteSpace(e.CommitSha)))
                throw new InvalidOperationException("A successful developer commit already exists; destructive retry is refused.");

            if (task.Status == AgentTaskStatus.Planning &&
                (!(executions.OrderByDescending(e => e.StartedAt).FirstOrDefault(e => e.Role == AgentRole.BackendDeveloper)?.Status is ExecutionStatus.Failed or ExecutionStatus.TimedOut)
                 || executions.Any(e => e.Status == ExecutionStatus.Succeeded)
                 || await _workspaceManager.HasDeveloperWorkspaceAsync(project, taskId, cancellationToken)))
                throw new InvalidOperationException("Planning task is not a safely recoverable stranded retry.");

            var agentValidation = await _agentRunner.ValidateAsync(cancellationToken);
            if (!agentValidation.IsReady)
                throw new InvalidOperationException($"Claude Code is not available: {agentValidation.ErrorMessage}");

            var workspace = await _workspaceManager.CreateForRetryAsync(project, taskId, cancellationToken);
            return await ExecuteInternalAsync(taskId, cancellationToken, true, workspace, human, () => executionStarted = true, task.Status);
        }
        catch (Exception ex)
        {
            if (executionStarted) throw;
            _logger.LogError(ex, "Retry preparation failed for task {TaskId}", taskId);
            try { await RecordRetryAsync(AuditEventType.TaskRetryPreflightFailed, taskId, human, CancellationToken.None); }
            catch (Exception auditError) { _logger.LogError(auditError, "Could not audit retry rejection for {TaskId}", taskId); }
            return new OrchestrationResult { Succeeded = false, Summary = ex is InvalidOperationException ? ex.Message : "Retry preparation failed. Task state was not advanced; inspect server logs.", ErrorMessage = "Retry preparation failed." };
        }
        finally
        {
            if (acquired) _concurrencyGuard.Release();
        }
    }

    private async Task RecordRetryAsync(string eventType, Guid taskId, HumanPrincipal human, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            ct => scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync(
                eventType, ActorType.Human, human.HumanId.ToString(), "AgentTask", taskId.ToString(),
                "Retry", new { taskId }, ct), token);
    }

    private async Task<OrchestrationResult> ExecuteInternalAsync(Guid taskId, CancellationToken cancellationToken, bool retryPrepared, WorkspaceInfo? preparedWorkspace, HumanPrincipal? human = null, Action? onStarted = null, AgentTaskStatus? expectedStatus = null)
    {
        // Load task
        AgentTask? task;
        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            task = await taskService.GetTaskAsync(taskId, cancellationToken);
        }

        if (task is null)
        {
            _logger.LogWarning("Task {TaskId} not found", taskId);
            return new OrchestrationResult { Succeeded = false, Summary = "Task not found.", ErrorMessage = "Task not found." };
        }

        if (expectedStatus is not null && task.Status != expectedStatus)
            throw new InvalidOperationException("Task status changed during retry preparation.");

        if (!retryPrepared && task.Status == AgentTaskStatus.Failed)
        {
            return new OrchestrationResult
            {
                Succeeded = false,
                Summary = "Task previously failed. Use /retry to explicitly start a new Developer attempt.",
                ErrorMessage = "Failed task requires explicit retry."
            };
        }

        if (!retryPrepared && task.Status != AgentTaskStatus.Created)
            return new OrchestrationResult { Succeeded = false, Summary = $"Task is already in {task.Status} and cannot be started with /run.", ErrorMessage = "Task cannot be started." };

        var project = _projectRegistry.Find(task.ProjectId);
        if (project is null)
        {
            _logger.LogWarning("Project {ProjectId} is not registered", task.ProjectId);
            return new OrchestrationResult { Succeeded = false, Summary = $"Project '{task.ProjectId}' is not registered.", ErrorMessage = "Unregistered project." };
        }

        _logger.LogInformation("Starting execution for task {TaskId} on project {ProjectId}", taskId, project.Id);

        WorkspaceInfo? workspace = null;
        AgentExecutionRecord execution;
        try
        {
            if (!retryPrepared)
            {
                var validation = await _agentRunner.ValidateAsync(cancellationToken);
                if (!validation.IsReady)
                    throw new InvalidOperationException($"Claude Code is not available: {validation.ErrorMessage}");
            }
            workspace = preparedWorkspace ?? await _workspaceManager.CreateAsync(project, taskId, cancellationToken);
            execution = new AgentExecutionRecord(taskId, project.Id, workspace.WorkspacePath, workspace.BranchName);
            using var scope = _scopeFactory.CreateScope();
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var executionRepo = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var current = await taskService.GetTaskAsync(taskId, token)
                    ?? throw new InvalidOperationException("Task disappeared during preparation.");
                if (current.Status != task.Status)
                    throw new InvalidOperationException("Task status changed during preparation.");
                var existingExecutions = await executionRepo.GetAllByTaskIdAsync(taskId, token);
                if (existingExecutions.Any(e => e.Status is ExecutionStatus.Queued or ExecutionStatus.Running))
                    throw new InvalidOperationException("An execution is already active for this task.");
                if (retryPrepared)
                {
                    if (existingExecutions.Any(e => !string.IsNullOrWhiteSpace(e.CommitSha))
                        || current.PullRequestNumber is not null || current.PullRequestUrl is not null
                        || current.MergedAt is not null || current.MergeCommitSha is not null
                        || await scope.ServiceProvider.GetRequiredService<IReleaseRepository>().GetByTaskIdAsync(taskId, token) is not null
                        || await scope.ServiceProvider.GetRequiredService<IPackageRepository>().GetByTaskIdAsync(taskId, token) is not null)
                        throw new InvalidOperationException("Task history changed during retry preparation.");
                    if (current.Status == AgentTaskStatus.Planning)
                        await taskService.TransitionAsync(taskId, AgentTaskStatus.Failed, token);
                    await taskService.RetryFailedTaskAsync(taskId, token);
                }
                else
                    await taskService.TransitionAsync(taskId, AgentTaskStatus.Planning, token);
                await taskService.TransitionAsync(taskId, AgentTaskStatus.AwaitingApproval, token);
                await taskService.TransitionAsync(taskId, AgentTaskStatus.Approved, token);
                await taskService.TransitionAsync(taskId, AgentTaskStatus.InProgress, token);
                await taskService.SetBranchNameAsync(taskId, workspace.BranchName, token);
                execution.MarkRunning();
                await executionRepo.AddAsync(execution, token);
                if (retryPrepared)
                {
                    var history = await executionRepo.GetAllByTaskIdAsync(taskId, token);
                    await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync(
                        AuditEventType.TaskRetryStarted, ActorType.Human, human!.HumanId.ToString(),
                        "AgentTask", taskId.ToString(), "Retry",
                        new { taskId, executionId = execution.Id, priorStatus = task.Status.ToString(), recoveredStrandedRetry = task.Status == AgentTaskStatus.Planning, attemptNumber = history.Count(e => e.Role == AgentRole.BackendDeveloper) }, token);
                }
                return execution;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Only a workspace returned by this preparation is eligible for cleanup.
            if (workspace is not null)
            {
                try { await _workspaceManager.RemoveAsync(workspace.WorkspacePath, CancellationToken.None); }
                catch (Exception cleanupError) { _logger.LogError(cleanupError, "Could not clean prepared workspace for {TaskId}", taskId); }
            }
            if (retryPrepared) throw;
            _logger.LogError(ex, "Execution preparation failed for {TaskId}", taskId);
            return new OrchestrationResult { Succeeded = false, Summary = ex is InvalidOperationException ? ex.Message : "Execution preparation failed. Check server logs.", ErrorMessage = "Preparation failed." };
        }

        onStarted?.Invoke();
        var opts = _executionOptions.Value;
        using var claudeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        claudeCts.CancelAfter(TimeSpan.FromMinutes(opts.ClaudeTimeoutMinutes));

        CodingAgentResult agentResult;
        try
        {
            agentResult = await _agentRunner.RunAsync(new CodingAgentRequest
            {
                TaskDescription = task.Description,
                WorkspacePath = workspace.WorkspacePath,
                ProjectId = project.Id
            }, claudeCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            execution.MarkTimedOut("Claude Code agent timed out.");
            await PersistAndFailAsync(execution, taskId, AgentTaskStatus.InProgress, cancellationToken);
            return new OrchestrationResult { Succeeded = false, Summary = "Agent timed out.", ErrorMessage = "Timed out." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Developer execution failed for {TaskId}", taskId);
            execution.Fail(ex is OperationCanceledException ? "Developer execution cancelled." : "Developer execution failed; inspect server logs.");
            await PersistAndFailAsync(execution, taskId, AgentTaskStatus.InProgress, CancellationToken.None);
            return new OrchestrationResult { Succeeded = false, Summary = execution.ErrorMessage!, ErrorMessage = execution.ErrorMessage };
        }

        if (!agentResult.Success)
        {
            execution.Fail(agentResult.ErrorMessage ?? "Agent run failed.");
            await PersistAndFailAsync(execution, taskId, AgentTaskStatus.InProgress, cancellationToken);
            return new OrchestrationResult { Succeeded = false, Summary = $"Agent failed: {agentResult.ErrorMessage}", ErrorMessage = agentResult.ErrorMessage };
        }

        // Transition to Testing phase
        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            await taskService.TransitionAsync(taskId, AgentTaskStatus.Testing, cancellationToken);
        }

        // Run dotnet build (independent validation — agent cannot review itself)
        var buildResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "dotnet",
            Arguments = ["build", "--no-restore", "-c", "Release"],
            WorkingDirectory = workspace.WorkspacePath,
            TimeoutMs = opts.BuildTimeoutMinutes * 60_000
        }, cancellationToken);

        // Run dotnet test
        var testResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "dotnet",
            Arguments = ["test", "--no-build", "-c", "Release"],
            WorkingDirectory = workspace.WorkspacePath,
            TimeoutMs = opts.TestTimeoutMinutes * 60_000
        }, cancellationToken);

        bool buildOk = buildResult.Success && !buildResult.TimedOut;
        bool testOk = testResult.Success && !testResult.TimedOut;

        execution.Complete(
            agentResult.Output,
            buildResult.StandardOutput + buildResult.StandardError,
            testResult.StandardOutput + testResult.StandardError,
            buildOk,
            testOk);

        await UpdateExecutionAsync(execution, cancellationToken);

        // Commit developer changes so QA/Reviewer see the verified state, not uncommitted files
        if (buildOk && testOk)
        {
            var shortId = taskId.ToString("N")[..8];
            try
            {
                var commitSha = await _workspaceManager.CommitAsync(
                    workspace.WorkspacePath,
                    $"rebelgent: implement task {shortId}",
                    cancellationToken);
                execution.SetCommitSha(commitSha);
                await UpdateExecutionAsync(execution, cancellationToken);
                _logger.LogInformation("Developer changes committed as {CommitSha} for task {TaskId}", commitSha, taskId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit developer changes for task {TaskId}", taskId);
                execution.Fail($"Commit failed: {ex.Message}");
                await PersistAndFailAsync(execution, taskId, AgentTaskStatus.Testing, cancellationToken);
                return new OrchestrationResult { Succeeded = false, Summary = "Developer changes could not be committed.", ErrorMessage = ex.Message };
            }
        }

        // Testing → Reviewing (if all pass) or Testing → Failed
        var finalStatus = buildOk && testOk ? AgentTaskStatus.Reviewing : AgentTaskStatus.Failed;
        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            await taskService.TransitionAsync(taskId, finalStatus, cancellationToken);
        }

        _logger.LogInformation("Execution complete for task {TaskId}: build={Build} test={Test}", taskId, buildOk, testOk);

        var summary = buildOk && testOk
            ? $"Agent completed. Build: PASS. Tests: PASS. Branch: {workspace.BranchName}"
            : $"Agent completed with failures. Build: {(buildOk ? "PASS" : "FAIL")}. Tests: {(testOk ? "PASS" : "FAIL")}.";

        return new OrchestrationResult { Succeeded = buildOk && testOk, Summary = summary };
    }

    private async Task UpdateExecutionAsync(AgentExecutionRecord execution, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var executionRepo = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
            await executionRepo.UpdateAsync(execution, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update execution record {ExecutionId}", execution.Id);
        }
    }

    private async Task FailTaskAsync(Guid taskId, AgentTaskStatus currentStatus, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            await taskService.TransitionAsync(taskId, AgentTaskStatus.Failed, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark task {TaskId} as failed (from {Status})", taskId, currentStatus);
        }
    }

    private async Task PersistAndFailAsync(AgentExecutionRecord execution, Guid taskId, AgentTaskStatus currentStatus, CancellationToken cancellationToken)
    {
        await UpdateExecutionAsync(execution, cancellationToken);
        await FailTaskAsync(taskId, currentStatus, cancellationToken);
    }
}
