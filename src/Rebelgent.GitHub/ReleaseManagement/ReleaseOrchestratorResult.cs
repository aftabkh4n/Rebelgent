using Rebelgent.Core.Domain;

namespace Rebelgent.GitHub.Release;

public sealed class ReleaseOrchestratorResult
{
    public bool Succeeded { get; init; }
    public string? Version { get; init; }
    public string? TagName { get; init; }
    public string? Title { get; init; }
    public string? GitHubReleaseUrl { get; init; }
    public ReleaseStatus? Status { get; init; }
    public string? ErrorMessage { get; init; }
    public string Summary { get; init; } = string.Empty;
}
