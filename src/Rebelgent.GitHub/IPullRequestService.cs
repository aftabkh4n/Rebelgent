namespace Rebelgent.GitHub;

/// <summary>
/// Abstracts GitHub pull request operations (push + create PR via gh CLI).
/// All input values come from registered project configuration — never from user-supplied text.
/// </summary>
public interface IPullRequestService
{
    Task<GitHubValidationResult> ValidateAsync(CancellationToken cancellationToken = default);

    Task<PullRequestCreatedResult> PushAndCreateAsync(PushAndCreateRequest request, CancellationToken cancellationToken = default);
}
