using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.Core.Domain;
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
        var acquired = await _concurrencyGuard.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
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
            return await ExecuteInternalAsync(taskId, cancellationToken);
        }
        finally
        {
            _concurrencyGuard.Release();
        }
    }

    private async Task<OrchestrationResult> ExecuteInternalAsync(Guid taskId, CancellationToken cancellationToken)
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

        var project = _projectRegistry.Find(task.ProjectId);
        if (project is null)
        {
            _logger.LogWarning("Project {ProjectId} is not registered", task.ProjectId);
            return new OrchestrationResult { Succeeded = false, Summary = $"Project '{task.ProjectId}' is not registered.", ErrorMessage = "Unregistered project." };
        }

        _logger.LogInformation("Starting execution for task {TaskId} on project {ProjectId}", taskId, project.Id);

        // Walk through the approval pipeline: Created→Planning→AwaitingApproval→Approved→InProgress
        // /run IS the explicit human approval gate — no further gate needed for M3
        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            await taskService.TransitionAsync(taskId, AgentTaskStatus.Planning, cancellationToken);
            await taskService.TransitionAsync(taskId, AgentTaskStatus.AwaitingApproval, cancellationToken);
            await taskService.TransitionAsync(taskId, AgentTaskStatus.Approved, cancellationToken);
            await taskService.TransitionAsync(taskId, AgentTaskStatus.InProgress, cancellationToken);
        }

        // Pre-flight: verify Claude is configured and reachable before touching the filesystem
        var agentValidation = await _agentRunner.ValidateAsync(cancellationToken);
        if (!agentValidation.IsReady)
        {
            _logger.LogError("Claude Code agent is not available for task {TaskId}: {Error}", taskId, agentValidation.ErrorMessage);
            await FailTaskAsync(taskId, AgentTaskStatus.InProgress, cancellationToken);
            return new OrchestrationResult
            {
                Succeeded = false,
                Summary = $"Claude Code is not available: {agentValidation.ErrorMessage}",
                ErrorMessage = agentValidation.ErrorMessage
            };
        }

        // Create workspace
        WorkspaceInfo workspace;
        try
        {
            workspace = await _workspaceManager.CreateAsync(project, taskId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workspace for task {TaskId}", taskId);
            await FailTaskAsync(taskId, AgentTaskStatus.InProgress, cancellationToken);
            return new OrchestrationResult { Succeeded = false, Summary = "Failed to create workspace.", ErrorMessage = ex.Message };
        }

        // Record branch name and create execution record
        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            await taskService.SetBranchNameAsync(taskId, workspace.BranchName, cancellationToken);
        }

        var execution = new AgentExecutionRecord(taskId, project.Id, workspace.WorkspacePath, workspace.BranchName);
        using (var scope = _scopeFactory.CreateScope())
        {
            var executionRepo = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
            await executionRepo.AddAsync(execution, cancellationToken);
        }

        // Run Claude Code agent
        execution.MarkRunning();
        await UpdateExecutionAsync(execution, cancellationToken);

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
