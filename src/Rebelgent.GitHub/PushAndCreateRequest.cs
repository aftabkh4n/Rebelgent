namespace Rebelgent.GitHub;

public sealed record PushAndCreateRequest
{
    public required string RepositoryPath { get; init; }
    public required string BranchName { get; init; }
    public required string RemoteName { get; init; }
    public required string BaseBranch { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? GitHubRepository { get; init; }
}
