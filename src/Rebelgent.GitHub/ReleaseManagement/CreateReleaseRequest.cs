namespace Rebelgent.GitHub.Release;

public sealed record CreateReleaseRequest
{
    public required string RepositoryPath { get; init; }
    public required string TagName { get; init; }
    public required string Title { get; init; }
    public required string Notes { get; init; }
    public required string TargetCommitSha { get; init; }
    public string? GitHubRepository { get; init; }
}
