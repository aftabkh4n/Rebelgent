namespace Rebelgent.GitHub.Release;

public interface IReleaseService
{
    Task<GitHubValidationResult> ValidateAsync(CancellationToken cancellationToken = default);

    Task<bool> TagExistsAsync(string tagName, string repositoryPath, string? gitHubRepository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the highest valid semver published in the repository (e.g. "1.2.3"),
    /// or null if no published releases exist or the query fails.
    /// Never throws — failures are logged and return null.
    /// </summary>
    Task<string?> GetLatestReleaseVersionAsync(string repositoryPath, string? gitHubRepository, CancellationToken cancellationToken = default);

    Task<ReleaseCreatedResult> CreateReleaseAsync(CreateReleaseRequest request, CancellationToken cancellationToken = default);
}
