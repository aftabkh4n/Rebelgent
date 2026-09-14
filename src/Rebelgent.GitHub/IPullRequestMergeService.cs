namespace Rebelgent.GitHub;

public interface IPullRequestMergeService
{
    Task<GitHubValidationResult> ValidateAsync(CancellationToken cancellationToken = default);

    Task<PrStateResult> GetPrStateAsync(int pullRequestNumber, string repositoryPath, string? gitHubRepository, CancellationToken cancellationToken = default);

    Task<PullRequestMergeResult> MergeAsync(MergeRequest request, CancellationToken cancellationToken = default);
}
