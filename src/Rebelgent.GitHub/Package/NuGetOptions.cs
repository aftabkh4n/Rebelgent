namespace Rebelgent.GitHub.Package;

/// <summary>
/// Configuration for the NuGet package publisher.
/// The API key is intentionally excluded — it must come from user-secrets ("NuGet:ApiKey")
/// or the environment variable NUGET_API_KEY and must never appear in config files or logs.
/// </summary>
public sealed class NuGetOptions
{
    public const string SectionName = "NuGet";

    /// <summary>
    /// NuGet feed URL or local folder path. Must be explicitly configured — there is no default.
    /// Examples: a private Azure Artifacts feed URL, or a local folder path for testing.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Set to <c>true</c> to allow publishing to a public nuget.org feed.
    /// Defaults to <c>false</c> as a safety guard. Must be explicitly enabled for production publishing.
    /// </summary>
    public bool AllowPublicPublish { get; set; } = false;
}
