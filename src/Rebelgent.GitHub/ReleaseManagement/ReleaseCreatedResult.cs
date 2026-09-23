namespace Rebelgent.GitHub.Release;

public sealed class ReleaseCreatedResult
{
    public bool Succeeded { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? ErrorMessage { get; init; }

    public static ReleaseCreatedResult Ok(string releaseUrl) => new() { Succeeded = true, ReleaseUrl = releaseUrl };
    public static ReleaseCreatedResult Fail(string error) => new() { Succeeded = false, ErrorMessage = error };
}
