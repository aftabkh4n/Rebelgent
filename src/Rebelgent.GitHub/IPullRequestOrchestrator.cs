namespace Rebelgent.GitHub;

/// <summary>
/// Drives the full PR creation pipeline: validates task state, checks QA/Reviewer results,
/// pushes the developer branch, creates a GitHub PR, and persists the result.
/// Triggered by explicit human /pr command — never automatically.
/// </summary>
public interface IPullRequestOrchestrator
{
    Task<PullRequestOrchestratorResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default);
}
