namespace Rebelgent.GitHub;

public sealed class PrStateResult
{
    public bool Succeeded { get; init; }
    public string? HeadBranch { get; init; }
    public string? BaseBranch { get; init; }
    public string? State { get; init; }
    public bool? Mergeable { get; init; }
    public string? ErrorMessage { get; init; }

    public static PrStateResult Ok(string headBranch, string baseBranch, string state, bool? mergeable) => new()
    {
        Succeeded = true,
        HeadBranch = headBranch,
        BaseBranch = baseBranch,
        State = state,
        Mergeable = mergeable
    };

    public static PrStateResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error
    };
}
