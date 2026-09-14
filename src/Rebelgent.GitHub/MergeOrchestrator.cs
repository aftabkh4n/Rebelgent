using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Projects;

namespace Rebelgent.GitHub;

/// <summary>
/// Validates task state, checks QA/Reviewer outcomes, validates the existing PR,
/// merges via squash, and persists the result.
/// Triggered by explicit human /merge command only — never automatically.
/// </summary>
public sealed class MergeOrchestrator : IMergeOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProjectRegistry _projectRegistry;
    private readonly IPullRequestMergeService _mergeService;
    private readonly ILogger<MergeOrchestrator> _logger;

    public MergeOrchestrator(
        IServiceScopeFactory scopeFactory,
        IProjectRegistry projectRegistry,
        IPullRequestMergeService mergeService,
        ILogger<MergeOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _projectRegistry = projectRegistry;
        _mergeService = mergeService;
        _logger = logger;
    }

    public async Task<MergeOrchestratorResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        AgentTask? task;
        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            task = await taskService.GetTaskAsync(taskId, cancellationToken);
        }

        if (task is null)
            return Fail("Task not found.");

        // Idempotent: already merged
        if (!string.IsNullOrWhiteSpace(task.MergeCommitSha))
        {
            _logger.LogInformation("PR already merged for task {TaskId}: {Sha}", taskId, task.MergeCommitSha);
            return new MergeOrchestratorResult
            {
                Succeeded = true,
                MergeCommitSha = task.MergeCommitSha,
                MergeMethod = task.MergeMethod,
                Summary = $"Pull request already merged via {task.MergeMethod}. Commit: {task.MergeCommitSha}"
            };
        }

        if (task.Status is not (AgentTaskStatus.AwaitingReview or AgentTaskStatus.Completed))
            return Fail($"Task is in status '{task.Status}'. Only tasks in AwaitingReview or Completed status can be merged.");

        if (string.IsNullOrWhiteSpace(task.BranchName))
            return Fail("Task has no branch name. Run the developer agent first.");

        if (task.PullRequestNumber is null || string.IsNullOrWhiteSpace(task.PullRequestUrl))
            return Fail("Task has no pull request. Run /pr first to create a pull request.");

        var project = _projectRegistry.Find(task.ProjectId);
        if (project is null)
            return Fail($"Project '{task.ProjectId}' is not registered.");

        AgentExecutionRecord? devExecution;
        AgentExecutionRecord? qaExecution;
        AgentExecutionRecord? reviewerExecution;

        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
            devExecution = await repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.BackendDeveloper, cancellationToken);
            qaExecution = await repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.QaEngineer, cancellationToken);
            reviewerExecution = await repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.CodeReviewer, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(devExecution?.CommitSha))
            return Fail("No verified developer commit SHA found. Run the developer agent first.");

        if (qaExecution is null || !FindingsContain(qaExecution.Findings, "QA_PASSED"))
            return Fail("QA did not pass. Cannot merge.");

        if (reviewerExecution is null || !FindingsContain(reviewerExecution.Findings, "REVIEW_APPROVED"))
            return Fail("Code review was not approved. Cannot merge.");

        var validation = await _mergeService.ValidateAsync(cancellationToken);
        if (!validation.IsReady)
        {
            _logger.LogError("GitHub CLI not ready: {Error}", validation.ErrorMessage);
            return Fail($"GitHub CLI not available: {validation.ErrorMessage}");
        }

        // Validate the current PR state before merging
        var prNumber = task.PullRequestNumber.Value;
        var prState = await _mergeService.GetPrStateAsync(prNumber, project.RepositoryPath, project.GitHubRepository, cancellationToken);

        if (!prState.Succeeded)
            return Fail(prState.ErrorMessage ?? "Failed to retrieve PR state.");

        if (!string.Equals(prState.HeadBranch, task.BranchName, StringComparison.OrdinalIgnoreCase))
            return Fail($"PR head branch '{prState.HeadBranch}' does not match task branch '{task.BranchName}'. Refusing to merge.");

        if (!string.Equals(prState.BaseBranch, project.DefaultBranch, StringComparison.OrdinalIgnoreCase))
            return Fail($"PR base branch '{prState.BaseBranch}' does not match project default branch '{project.DefaultBranch}'. Refusing to merge.");

        if (!string.Equals(prState.State, "OPEN", StringComparison.OrdinalIgnoreCase))
            return Fail($"Pull request is not open (state: {prState.State}). Cannot merge a closed or merged PR.");

        if (prState.Mergeable == false)
            return Fail("Pull request has merge conflicts. Resolve conflicts before merging.");

        var mergeResult = await _mergeService.MergeAsync(new MergeRequest
        {
            RepositoryPath = project.RepositoryPath,
            PullRequestNumber = prNumber,
            ExpectedHeadBranch = task.BranchName,
            ExpectedBaseBranch = project.DefaultBranch,
            GitHubRepository = project.GitHubRepository
        }, cancellationToken);

        if (!mergeResult.Succeeded)
        {
            _logger.LogError("Merge failed for task {TaskId} PR #{Number}: {Error}", taskId, prNumber, mergeResult.ErrorMessage);
            return Fail($"Merge failed: {mergeResult.ErrorMessage ?? "unknown error"}");
        }

        var commitSha = mergeResult.MergeCommitSha ?? string.Empty;
        var mergeMethod = mergeResult.MergeMethod ?? "squash";

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            await taskService.SetMergeInfoAsync(taskId, commitSha, mergeMethod, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist merge info for task {TaskId}", taskId);
        }

        _logger.LogInformation("PR #{Number} merged for task {TaskId} via {Method} at {Sha}", prNumber, taskId, mergeMethod, commitSha);

        return new MergeOrchestratorResult
        {
            Succeeded = true,
            MergeCommitSha = commitSha,
            MergeMethod = mergeMethod,
            Summary = $"Pull request #{prNumber} merged via {mergeMethod}.\nCommit: {commitSha}"
        };
    }

    private static bool FindingsContain(string? findings, string marker) =>
        findings is not null && findings.Contains(marker, StringComparison.OrdinalIgnoreCase);

    private static MergeOrchestratorResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error,
        Summary = error
    };
}
