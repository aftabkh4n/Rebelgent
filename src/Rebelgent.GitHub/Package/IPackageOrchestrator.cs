namespace Rebelgent.GitHub.Package;

/// <summary>
/// Provider-independent interface for the NuGet package publishing pipeline.
/// Prepare → preview → explicit human approval → publish.
/// </summary>
public interface IPackageOrchestrator
{
    /// <summary>Pack the project, validate metadata and version, preview for human review.</summary>
    Task<PackageOrchestratorResult> PrepareAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish the prepared package to the configured feed.
    /// This is the ONLY method that may publish a package — never called automatically.
    /// </summary>
    Task<PackageOrchestratorResult> ApproveAndPublishAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Return the current package status without re-running pack.</summary>
    Task<PackageOrchestratorResult> GetInfoAsync(Guid taskId, CancellationToken cancellationToken = default);
}
