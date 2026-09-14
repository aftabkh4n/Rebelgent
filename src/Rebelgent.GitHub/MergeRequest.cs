namespace Rebelgent.GitHub;

public sealed record MergeRequest
{
    public required string RepositoryPath { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadBranch { get; init; }
    public required string ExpectedBaseBranch { get; init; }
    public string? GitHubRepository { get; init; }
}
