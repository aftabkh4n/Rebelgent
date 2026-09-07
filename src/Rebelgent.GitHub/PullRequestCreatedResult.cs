namespace Rebelgent.GitHub;

public sealed class PullRequestCreatedResult
{
    public bool Succeeded { get; init; }
    public int? PullRequestNumber { get; init; }
    public string? PullRequestUrl { get; init; }
    public string? ErrorMessage { get; init; }
}
