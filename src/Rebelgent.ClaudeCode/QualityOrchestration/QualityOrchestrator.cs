using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Agents;
using Rebelgent.Orchestration.Orchestrator;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.ClaudeCode.QualityOrchestration;

/// <summary>
/// Drives the QA and code review pipeline. Runs AFTER the developer agent completes.
/// Developer must never QA or review its own work — this orchestrator uses separate agents.
/// </summary>
public sealed class QualityOrchestrator : IQualityOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProjectRegistry _projectRegistry;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ICodingAgentRunner _agentRunner;
    private readonly ILogger<QualityOrchestrator> _logger;

    private const int QaTimeoutMs = 10 * 60_000;
    private const int ReviewerTimeoutMs = 10 * 60_000;

    public QualityOrchestrator(
        IServiceScopeFactory scopeFactory,
        IProjectRegistry projectRegistry,
        IWorkspaceManager workspaceManager,
        ICodingAgentRunner agentRunner,
        ILogger<QualityOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _projectRegistry = projectRegistry;
        _workspaceManager = workspaceManager;
        _agentRunner = agentRunner;
        _logger = logger;
    }

    public async Task<QualityOrchestrationResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        AgentTask? task;
        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            task = await taskService.GetTaskAsync(taskId, cancellationToken);
        }

        if (task is null)
        {
            _logger.LogWarning("QualityOrchestrator: Task {TaskId} not found", taskId);
            return Fail("Task not found.");
        }

        if (string.IsNullOrWhiteSpace(task.BranchName))
        {
            _logger.LogWarning("QualityOrchestrator: Task {TaskId} has no branch name", taskId);
            return Fail("Task has no branch name. Run the developer agent first.");
        }

        var project = _projectRegistry.Find(task.ProjectId);
        if (project is null)
        {
            _logger.LogWarning("QualityOrchestrator: Project {ProjectId} not registered", task.ProjectId);
            return Fail($"Project '{task.ProjectId}' is not registered.");
        }

        var validation = await _agentRunner.ValidateAsync(cancellationToken);
        if (!validation.IsReady)
        {
            _logger.LogError("QualityOrchestrator: Claude not ready: {Error}", validation.ErrorMessage);
            return Fail($"Claude Code is not available: {validation.ErrorMessage}");
        }

        // Require the exact developer commit SHA — QA/Reviewer must never inspect unverified or uncommitted state
        string developerCommitSha;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
            var devExecution = await repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.BackendDeveloper, cancellationToken);
            var sha = devExecution?.CommitSha;

            if (string.IsNullOrWhiteSpace(sha))
            {
                _logger.LogError("QualityOrchestrator: No valid developer commit SHA for task {TaskId}. QA/Reviewer refused.", taskId);
                return Fail("No verified developer commit SHA found. Run the developer agent first and ensure it completes with a successful build and commit.");
            }

            developerCommitSha = sha;
        }

        // ── QA Phase ─────────────────────────────────────────────────────────
        _logger.LogInformation("QualityOrchestrator: Starting QA for task {TaskId}", taskId);

        string? qaWorkspacePath = null;
        AgentExecutionRecord? qaExecution = null;
        string? qaFindings = null;
        QaOutcome qaOutcome;

        try
        {
            var qaWorkspace = await _workspaceManager.CreateFromBranchAsync(
                project, task.BranchName, "qa", developerCommitSha, cancellationToken);
            qaWorkspacePath = qaWorkspace.WorkspacePath;

            qaExecution = new AgentExecutionRecord(taskId, project.Id, qaWorkspace.WorkspacePath, qaWorkspace.BranchName,
                AgentRole.QaEngineer, "ClaudeCode");
            await PersistExecutionAsync(qaExecution, cancellationToken);
            qaExecution.MarkRunning();
            await UpdateExecutionAsync(qaExecution, cancellationToken);

            var qaPrompt = QaPromptBuilder.Build(task.Description, qaWorkspace.WorkspacePath, project.Id);
            var qaResult = await _agentRunner.InvokeAsync(qaPrompt, qaWorkspace.WorkspacePath, QaTimeoutMs, cancellationToken);

            if (qaResult.TimedOut)
            {
                qaExecution.MarkTimedOut("QA agent timed out.");
                await UpdateExecutionAsync(qaExecution, cancellationToken);
                qaOutcome = QaOutcome.TimedOut;
            }
            else if (!qaResult.Success)
            {
                qaExecution.Fail(qaResult.ErrorMessage ?? "QA agent failed.");
                await UpdateExecutionAsync(qaExecution, cancellationToken);
                qaOutcome = QaOutcome.Failed;
                qaFindings = qaResult.Output;
            }
            else
            {
                var output = qaResult.Output ?? string.Empty;
                var passed = output.Contains("QA_PASSED", StringComparison.OrdinalIgnoreCase);
                qaOutcome = passed ? QaOutcome.Passed : QaOutcome.Failed;
                qaFindings = output;
                qaExecution.CompleteWithFindings(output, output);
                await UpdateExecutionAsync(qaExecution, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QualityOrchestrator: QA phase failed for task {TaskId}", taskId);
            if (qaExecution is not null)
            {
                qaExecution.Fail(ex.Message);
                await UpdateExecutionAsync(qaExecution, cancellationToken);
            }
            qaOutcome = QaOutcome.Failed;
            qaFindings = ex.Message;
        }
        finally
        {
            if (qaWorkspacePath is not null)
                await SafeRemoveWorkspaceAsync(qaWorkspacePath, cancellationToken);
        }

        // ── Reviewer Phase ────────────────────────────────────────────────────
        _logger.LogInformation("QualityOrchestrator: Starting Code Review for task {TaskId}", taskId);

        string? reviewerWorkspacePath = null;
        AgentExecutionRecord? reviewerExecution = null;
        string? reviewFindings = null;
        ReviewDecision reviewDecision;

        try
        {
            var reviewerWorkspace = await _workspaceManager.CreateFromBranchAsync(
                project, task.BranchName, "reviewer", developerCommitSha, cancellationToken);
            reviewerWorkspacePath = reviewerWorkspace.WorkspacePath;

            reviewerExecution = new AgentExecutionRecord(taskId, project.Id, reviewerWorkspace.WorkspacePath, reviewerWorkspace.BranchName,
                AgentRole.CodeReviewer, "ClaudeCode");
            await PersistExecutionAsync(reviewerExecution, cancellationToken);
            reviewerExecution.MarkRunning();
            await UpdateExecutionAsync(reviewerExecution, cancellationToken);

            var reviewPrompt = ReviewerPromptBuilder.Build(task.Description, reviewerWorkspace.WorkspacePath, project.Id, qaFindings);
            var reviewResult = await _agentRunner.InvokeAsync(reviewPrompt, reviewerWorkspace.WorkspacePath, ReviewerTimeoutMs, cancellationToken);

            if (reviewResult.TimedOut)
            {
                reviewerExecution.MarkTimedOut("Reviewer agent timed out.");
                await UpdateExecutionAsync(reviewerExecution, cancellationToken);
                reviewDecision = ReviewDecision.TimedOut;
            }
            else if (!reviewResult.Success)
            {
                reviewerExecution.Fail(reviewResult.ErrorMessage ?? "Reviewer agent failed.");
                await UpdateExecutionAsync(reviewerExecution, cancellationToken);
                reviewDecision = ReviewDecision.ChangesRequested;
                reviewFindings = reviewResult.Output;
            }
            else
            {
                var output = reviewResult.Output ?? string.Empty;
                var approved = output.Contains("REVIEW_APPROVED", StringComparison.OrdinalIgnoreCase);
                reviewDecision = approved ? ReviewDecision.Approved : ReviewDecision.ChangesRequested;
                reviewFindings = output;
                reviewerExecution.CompleteWithFindings(output, output);
                await UpdateExecutionAsync(reviewerExecution, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QualityOrchestrator: Reviewer phase failed for task {TaskId}", taskId);
            if (reviewerExecution is not null)
            {
                reviewerExecution.Fail(ex.Message);
                await UpdateExecutionAsync(reviewerExecution, cancellationToken);
            }
            reviewDecision = ReviewDecision.ChangesRequested;
            reviewFindings = ex.Message;
        }
        finally
        {
            if (reviewerWorkspacePath is not null)
                await SafeRemoveWorkspaceAsync(reviewerWorkspacePath, cancellationToken);
        }

        // Transition based on review decision — human can further action via /review approve|reject
        var nextStatus = reviewDecision switch
        {
            ReviewDecision.Approved => AgentTaskStatus.AwaitingReview,
            ReviewDecision.ChangesRequested => AgentTaskStatus.ChangesRequested,
            _ => AgentTaskStatus.Failed
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            await taskService.TransitionAsync(taskId, nextStatus, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QualityOrchestrator: Could not transition task {TaskId} to {Status}", taskId, nextStatus);
        }

        var succeeded = qaOutcome == QaOutcome.Passed && reviewDecision == ReviewDecision.Approved;

        _logger.LogInformation("QualityOrchestrator: Complete for task {TaskId} — QA={QaOutcome} Review={ReviewDecision}", taskId, qaOutcome, reviewDecision);

        return new QualityOrchestrationResult
        {
            Succeeded = succeeded,
            QaOutcome = qaOutcome,
            ReviewDecision = reviewDecision,
            QaFindings = qaFindings,
            ReviewFindings = reviewFindings,
            Summary = BuildSummary(qaOutcome, reviewDecision)
        };
    }

    private static string BuildSummary(QaOutcome qa, ReviewDecision review)
    {
        var qaLabel = qa switch
        {
            QaOutcome.Passed => "PASSED",
            QaOutcome.Failed => "FAILED",
            QaOutcome.TimedOut => "TIMED OUT",
            _ => "UNKNOWN"
        };
        var reviewLabel = review switch
        {
            ReviewDecision.Approved => "APPROVED",
            ReviewDecision.ChangesRequested => "CHANGES REQUESTED",
            ReviewDecision.TimedOut => "TIMED OUT",
            _ => "UNKNOWN"
        };
        return $"QA: {qaLabel}. Review: {reviewLabel}. Use /review approve|reject|rerun to decide next steps.";
    }

    private async Task PersistExecutionAsync(AgentExecutionRecord execution, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
            await repo.AddAsync(execution, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QualityOrchestrator: Failed to persist execution {ExecutionId}", execution.Id);
        }
    }

    private async Task UpdateExecutionAsync(AgentExecutionRecord execution, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
            await repo.UpdateAsync(execution, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QualityOrchestrator: Failed to update execution {ExecutionId}", execution.Id);
        }
    }

    private async Task SafeRemoveWorkspaceAsync(string workspacePath, CancellationToken cancellationToken)
    {
        try
        {
            await _workspaceManager.RemoveAsync(workspacePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QualityOrchestrator: Failed to remove workspace {WorkspacePath}", workspacePath);
        }
    }

    private static QualityOrchestrationResult Fail(string error) => new()
    {
        Succeeded = false,
        QaOutcome = QaOutcome.Failed,
        ReviewDecision = ReviewDecision.ChangesRequested,
        ErrorMessage = error,
        Summary = error
    };
}
