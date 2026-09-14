namespace Rebelgent.Core.Publishing;

public sealed class PackagePrepareResult
{
    public bool Succeeded { get; private init; }
    public string? PackageId { get; private init; }
    public string? PackageVersion { get; private init; }

    /// <summary>Absolute path to the produced .nupkg file.</summary>
    public string? PackagePath { get; private init; }

    public string? ErrorMessage { get; private init; }

    public static PackagePrepareResult Ok(string packageId, string version, string packagePath) => new()
    {
        Succeeded = true,
        PackageId = packageId,
        PackageVersion = version,
        PackagePath = packagePath
    };

    public static PackagePrepareResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error
    };
}
