namespace Rebelgent.GitHub;

public sealed class PullRequestMergeResult
{
    public bool Succeeded { get; init; }
    public string? MergeCommitSha { get; init; }
    public string? MergeMethod { get; init; }
    public string? ErrorMessage { get; init; }

    public static PullRequestMergeResult Ok(string mergeCommitSha, string mergeMethod) => new()
    {
        Succeeded = true,
        MergeCommitSha = mergeCommitSha,
        MergeMethod = mergeMethod
    };

    public static PullRequestMergeResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error
    };
}
