namespace Rebelgent.GitHub;

public sealed class GitHubValidationResult
{
    public bool IsReady { get; init; }
    public string? ErrorMessage { get; init; }

    public static GitHubValidationResult Ready() => new() { IsReady = true };
    public static GitHubValidationResult Unavailable(string reason) => new() { IsReady = false, ErrorMessage = reason };
}
