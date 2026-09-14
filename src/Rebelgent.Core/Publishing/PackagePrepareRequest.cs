namespace Rebelgent.Core.Publishing;

/// <summary>Input for packing a project into a NuGet-compatible package.</summary>
public sealed record PackagePrepareRequest
{
    /// <summary>Absolute path to the repository root. Working directory for the pack command.</summary>
    public required string RepositoryPath { get; init; }

    /// <summary>
    /// Relative or absolute path to the .csproj or .sln to pack.
    /// Null means dotnet pack runs at <see cref="RepositoryPath"/> and discovers the project automatically.
    /// </summary>
    public string? ProjectFilePath { get; init; }

    public string Configuration { get; init; } = "Release";

    /// <summary>Directory where the .nupkg file will be written.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>
    /// If set, the built package version must match this value exactly.
    /// Pass the GitHub release version so a mismatch is caught before publishing.
    /// </summary>
    public string? ExpectedVersion { get; init; }

    /// <summary>
    /// If set, used to disambiguate when multiple .nupkg files with the same version exist in the output directory.
    /// Matches the NuGet package ID (e.g. "Rebelgent.Core").
    /// </summary>
    public string? ExpectedPackageId { get; init; }

    /// <summary>
    /// When set, overrides the project's version at pack time via <c>/p:PackageVersion</c> and <c>/p:Version</c>
    /// MSBuild properties. Must match the GitHub release version so the produced .nupkg has the correct version.
    /// Required for release packaging — prevents the project's csproj default from silently producing the wrong version.
    /// </summary>
    public string? PackageVersion { get; init; }
}
