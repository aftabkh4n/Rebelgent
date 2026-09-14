namespace Rebelgent.Core.Publishing;

/// <summary>Input for publishing a pre-built .nupkg file to a package feed.</summary>
public sealed record PackagePublishRequest
{
    /// <summary>Absolute path to the .nupkg file produced by <see cref="IPackagePublisher.PrepareAsync"/>.</summary>
    public required string PackagePath { get; init; }
}
