using Rebelgent.Core.Domain;

namespace Rebelgent.GitHub.Package;

public sealed class PackageOrchestratorResult
{
    public bool Succeeded { get; init; }
    public string? PackageId { get; init; }
    public string? PackageVersion { get; init; }
    public string? PackagePath { get; init; }
    public PackageStatus? Status { get; init; }
    public DateTimeOffset? PreparedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public string Summary { get; init; } = string.Empty;
}
