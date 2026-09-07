using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Projects;

namespace Rebelgent.GitHub;

/// <summary>
/// Validates task state, checks QA/Reviewer outcomes, pushes the developer branch,
/// creates a GitHub PR, and persists the result.
/// Triggered by explicit human /pr command only — never automatically.
/// </summary>
public sealed class PullRequestOrchestrator : IPullRequestOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProjectRegistry _projectRegistry;
    private readonly IPullRequestService _pullRequestService;
    private readonly ILogger<PullRequestOrchestrator> _logger;

    public PullRequestOrchestrator(
        IServiceScopeFactory scopeFactory,
        IProjectRegistry projectRegistry,
        IPullRequestService pullRequestService,
        ILogger<PullRequestOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _projectRegistry = projectRegistry;
        _pullRequestService = pullRequestService;
        _logger = logger;
    }

    public async Task<PullRequestOrchestratorResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        AgentTask? task;
        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            task = await taskService.GetTaskAsync(taskId, cancellationToken);
        }

        if (task is null)
            return Fail("Task not found.");

        if (!string.IsNullOrWhiteSpace(task.PullRequestUrl))
        {
            _logger.LogInformation("PR already created for task {TaskId}: {Url}", taskId, task.PullRequestUrl);
            return new PullRequestOrchestratorResult
            {
                Succeeded = true,
                PullRequestNumber = task.PullRequestNumber,
                PullRequestUrl = task.PullRequestUrl,
                Summary = $"Pull request already exists: #{task.PullRequestNumber} {task.PullRequestUrl}"
            };
        }

        if (task.Status is not (AgentTaskStatus.AwaitingReview or AgentTaskStatus.Completed))
            return Fail($"Task is in status '{task.Status}'. Only tasks in AwaitingReview or Completed status can have a PR created. Run /review first.");

        if (string.IsNullOrWhiteSpace(task.BranchName))
            return Fail("Task has no branch name. Run the developer agent first.");

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
            return Fail("QA did not pass. Run /review to trigger QA and code review.");

        if (reviewerExecution is null || !FindingsContain(reviewerExecution.Findings, "REVIEW_APPROVED"))
            return Fail("Code review was not approved. Run /review and ensure the reviewer approves.");

        var validation = await _pullRequestService.ValidateAsync(cancellationToken);
        if (!validation.IsReady)
        {
            _logger.LogError("GitHub CLI not ready: {Error}", validation.ErrorMessage);
            return Fail($"GitHub CLI not available: {validation.ErrorMessage}");
        }

        var body = BuildPrBody(task, devExecution, qaExecution, reviewerExecution);

        var prResult = await _pullRequestService.PushAndCreateAsync(new PushAndCreateRequest
        {
            RepositoryPath = project.RepositoryPath,
            BranchName = task.BranchName,
            RemoteName = project.RemoteName,
            BaseBranch = project.DefaultBranch,
            Title = task.Title,
            Body = body,
            GitHubRepository = project.GitHubRepository
        }, cancellationToken);

        if (!prResult.Succeeded)
        {
            _logger.LogError("PR creation failed for task {TaskId}: {Error}", taskId, prResult.ErrorMessage);
            return Fail(prResult.ErrorMessage ?? "PR creation failed.");
        }

        var prNumber = prResult.PullRequestNumber ?? 0;
        var prUrl = prResult.PullRequestUrl ?? string.Empty;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            await taskService.SetPullRequestInfoAsync(taskId, prNumber, prUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist PR info for task {TaskId}", taskId);
        }

        _logger.LogInformation("PR #{Number} created for task {TaskId}: {Url}", prNumber, taskId, prUrl);

        return new PullRequestOrchestratorResult
        {
            Succeeded = true,
            PullRequestNumber = prNumber,
            PullRequestUrl = prUrl,
            Summary = $"Pull request created: #{prNumber} {prUrl}\nMerge requires human action."
        };
    }

    private static bool FindingsContain(string? findings, string marker) =>
        findings is not null && findings.Contains(marker, StringComparison.OrdinalIgnoreCase);

    private static string BuildPrBody(AgentTask task, AgentExecutionRecord dev, AgentExecutionRecord qa, AgentExecutionRecord reviewer)
    {
        var shortId = task.Id.ToString("N")[..8];
        return $"""
            ## Rebelgent Task [{shortId}]

            **Task:** {task.Title}
            **Description:** {task.Description}

            ## Results

            | Stage | Outcome |
            |-------|---------|
            | Developer | Build: {(dev.BuildSucceeded == true ? "OK" : "?")} / Tests: {(dev.TestsSucceeded == true ? "OK" : "?")} |
            | QA | {(FindingsContain(qa.Findings, "QA_PASSED") ? "PASSED" : "FAILED")} |
            | Code Review | {(FindingsContain(reviewer.Findings, "REVIEW_APPROVED") ? "APPROVED" : "CHANGES REQUESTED")} |

            ## Notes

            Created by Rebelgent. Merge requires human action.
            Developer commit: `{dev.CommitSha}`
            """;
    }

    private static PullRequestOrchestratorResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error,
        Summary = error
    };
}
