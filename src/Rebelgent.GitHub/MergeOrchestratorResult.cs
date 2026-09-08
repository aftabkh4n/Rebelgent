namespace Rebelgent.GitHub;

public sealed class MergeOrchestratorResult
{
    public bool Succeeded { get; init; }
    public string? MergeCommitSha { get; init; }
    public string? MergeMethod { get; init; }
    public string? ErrorMessage { get; init; }
    public string Summary { get; init; } = string.Empty;
}
