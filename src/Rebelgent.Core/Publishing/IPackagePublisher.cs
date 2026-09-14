using Rebelgent.Core.Publishing;

namespace Rebelgent.Core.Publishing;

/// <summary>
/// Provider-independent interface for packing and publishing a software package.
/// Implementations must never log API keys or credentials.
/// </summary>
public interface IPackagePublisher
{
    Task<PackagePrepareResult> PrepareAsync(PackagePrepareRequest request, CancellationToken cancellationToken = default);

    Task<PackagePublishedResult> PublishAsync(PackagePublishRequest request, CancellationToken cancellationToken = default);
}
