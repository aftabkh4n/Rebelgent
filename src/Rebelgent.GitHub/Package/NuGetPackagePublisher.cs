using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.Core.Publishing;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.GitHub.Package;

/// <summary>
/// Publishes NuGet packages using the dotnet CLI (dotnet pack / dotnet nuget push).
/// Never invokes cmd.exe, powershell.exe, or bash as intermediaries.
/// Never logs the NuGet API key — it is masked in all log output.
/// API key is read at publish time from user-secrets ("NuGet:ApiKey") or NUGET_API_KEY env var.
/// </summary>
public sealed class NuGetPackagePublisher : IPackagePublisher
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<NuGetOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NuGetPackagePublisher> _logger;

    public NuGetPackagePublisher(
        IProcessRunner processRunner,
        IOptions<NuGetOptions> options,
        IConfiguration configuration,
        ILogger<NuGetPackagePublisher> logger)
    {
        _processRunner = processRunner;
        _options = options;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PackagePrepareResult> PrepareAsync(PackagePrepareRequest request, CancellationToken cancellationToken = default)
    {
        // Safety: output directory must be a subdirectory of the repository path to prevent path escape
        var resolvedRepo = Path.GetFullPath(request.RepositoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolvedOutput = Path.GetFullPath(request.OutputDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!resolvedOutput.StartsWith(resolvedRepo + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return PackagePrepareResult.Fail(
                $"Output directory '{request.OutputDirectory}' is outside the repository path '{request.RepositoryPath}'. " +
                "Package output must stay within the repository worktree.");
        }

        // Validate the package version override before touching the filesystem or running any process
        if (request.PackageVersion is not null && !IsValidNuGetVersion(request.PackageVersion))
        {
            return PackagePrepareResult.Fail(
                $"PackageVersion '{request.PackageVersion}' is not a valid NuGet version string (expected e.g. '1.2.3').");
        }

        if (!Directory.Exists(request.OutputDirectory))
            Directory.CreateDirectory(request.OutputDirectory);

        var args = new List<string> { "pack" };
        if (!string.IsNullOrWhiteSpace(request.ProjectFilePath))
            args.Add(request.ProjectFilePath);
        args.AddRange(["--configuration", request.Configuration, "--output", request.OutputDirectory]);

        // Inject the release version at pack time so the produced .nupkg matches the GitHub release,
        // overriding whatever version is defined in the project file.
        if (!string.IsNullOrWhiteSpace(request.PackageVersion))
        {
            args.Add($"/p:PackageVersion={request.PackageVersion}");
            args.Add($"/p:Version={request.PackageVersion}");
        }

        _logger.LogInformation("Running dotnet pack for {Project} → {OutputDir}",
            request.ProjectFilePath ?? "(discovery)", request.OutputDirectory);

        var packResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "dotnet",
            Arguments = args,
            WorkingDirectory = request.RepositoryPath,
            TimeoutMs = 120_000
        }, cancellationToken);

        if (!packResult.Success)
        {
            _logger.LogError("dotnet pack failed: {Error}", packResult.StandardError);
            return PackagePrepareResult.Fail($"dotnet pack failed: {packResult.StandardError}");
        }

        // PackageVersion takes precedence over ExpectedVersion for all post-pack verification
        var effectiveVersion = request.PackageVersion ?? request.ExpectedVersion;

        // Find .nupkg files in the output directory, excluding *.symbols.nupkg and *.snupkg
        var nupkgFiles = Directory.GetFiles(request.OutputDirectory, "*.nupkg")
            .Where(f => !Path.GetFileName(f).EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (nupkgFiles.Length == 0)
            return PackagePrepareResult.Fail(
                $"dotnet pack succeeded but no .nupkg file was found in '{request.OutputDirectory}'. " +
                "Symbols-only packages (.symbols.nupkg) are excluded.");

        // Disambiguate when multiple packages exist
        if (nupkgFiles.Length > 1)
        {
            if (effectiveVersion is null)
                return PackagePrepareResult.Fail(
                    $"Multiple .nupkg files found in the output directory and no expected version was specified. " +
                    $"Found: {string.Join(", ", nupkgFiles.Select(Path.GetFileName))}");

            var versionSuffix = $".{effectiveVersion}.nupkg";
            var byVersion = nupkgFiles
                .Where(f => Path.GetFileName(f).EndsWith(versionSuffix, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (byVersion.Length == 0)
                return PackagePrepareResult.Fail(
                    $"Multiple .nupkg files found but none match version '{effectiveVersion}'. " +
                    $"Found: {string.Join(", ", nupkgFiles.Select(Path.GetFileName))}");

            if (byVersion.Length > 1)
            {
                if (request.ExpectedPackageId is null)
                    return PackagePrepareResult.Fail(
                        $"Multiple .nupkg files with version '{effectiveVersion}' found. " +
                        "Set NuGetPackageId in project configuration to disambiguate. " +
                        $"Found: {string.Join(", ", byVersion.Select(Path.GetFileName))}");

                var expectedFileName = $"{request.ExpectedPackageId}.{effectiveVersion}.nupkg";
                byVersion = byVersion
                    .Where(f => string.Equals(Path.GetFileName(f), expectedFileName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (byVersion.Length != 1)
                    return PackagePrepareResult.Fail(
                        $"Cannot uniquely identify package '{request.ExpectedPackageId}' version '{effectiveVersion}'. " +
                        $"Found: {string.Join(", ", nupkgFiles.Select(Path.GetFileName))}");
            }

            nupkgFiles = byVersion;
        }

        var nupkgPath = nupkgFiles[0];
        string packageId;
        string packageVersion;

        if (effectiveVersion is not null)
        {
            var baseName = Path.GetFileNameWithoutExtension(nupkgPath);
            var expectedSuffix = $".{effectiveVersion}";

            if (!baseName.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return PackagePrepareResult.Fail(
                    $"Package version mismatch: produced '{Path.GetFileName(nupkgPath)}' " +
                    $"but expected version '{effectiveVersion}'.");
            }

            packageId = baseName[..^expectedSuffix.Length];
            packageVersion = effectiveVersion;
        }
        else
        {
            if (!TryParseNupkgName(Path.GetFileName(nupkgPath), out packageId, out packageVersion))
                return PackagePrepareResult.Fail($"Could not parse package ID/version from '{Path.GetFileName(nupkgPath)}'.");
        }

        if (string.IsNullOrWhiteSpace(packageId))
            return PackagePrepareResult.Fail("Package ID could not be determined from the .nupkg filename.");

        _logger.LogInformation("Package prepared: {Id} {Version} at {Path}", packageId, packageVersion, nupkgPath);
        return PackagePrepareResult.Ok(packageId, packageVersion, nupkgPath);
    }

    public async Task<PackagePublishedResult> PublishAsync(PackagePublishRequest request, CancellationToken cancellationToken = default)
    {
        var source = _options.Value.Source;

        // Source must be explicitly configured — no implicit fallback to nuget.org
        if (string.IsNullOrWhiteSpace(source))
            return PackagePublishedResult.Fail(
                "NuGet source is not configured. Set 'NuGet:Source' in configuration.");

        // Block publishing to public nuget.org unless explicitly opted in
        var isPublicNuGetOrg = source.Contains("nuget.org", StringComparison.OrdinalIgnoreCase);
        if (isPublicNuGetOrg && !_options.Value.AllowPublicPublish)
            return PackagePublishedResult.Fail(
                "Publishing to nuget.org is blocked. Set NuGet:AllowPublicPublish = true to enable publishing to a public feed.");

        // Local folder sources (non-HTTP) do not require an API key
        var isLocalSource = !source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                         && !source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        // Resolve API key at publish time — never stored, never logged
        var apiKey = _configuration["NuGet:ApiKey"]
            ?? Environment.GetEnvironmentVariable("NUGET_API_KEY");

        if (!isLocalSource && string.IsNullOrWhiteSpace(apiKey))
            return PackagePublishedResult.Fail(
                "NuGet API key not configured. Set 'NuGet:ApiKey' via dotnet user-secrets or the NUGET_API_KEY environment variable.");

        _logger.LogInformation("Publishing {Package} to {Source}", request.PackagePath, source);

        // Build args: include --api-key only when a key is available (required for HTTP feeds;
        // optional but harmless for local folders). Key index is computed dynamically so it can
        // be masked in log output without hard-coding a position.
        var args = new List<string> { "nuget", "push", request.PackagePath };
        var secretIndices = new List<int>();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            args.Add("--api-key");
            args.Add(apiKey);
            secretIndices.Add(args.Count - 1); // mask the key value, never the --api-key flag itself
        }

        args.AddRange(["--source", source]);

        var pushResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "dotnet",
            Arguments = args,
            TimeoutMs = 120_000,
            SecretArgumentIndices = secretIndices
        }, cancellationToken);

        if (!pushResult.Success)
        {
            _logger.LogError("dotnet nuget push failed: {Error}", pushResult.StandardError);
            return PackagePublishedResult.Fail($"dotnet nuget push failed: {pushResult.StandardError}");
        }

        return PackagePublishedResult.Ok();
    }

    // Accepts anything that looks like major.minor[...] — dotnet pack itself will reject truly invalid versions.
    private static bool IsValidNuGetVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        var parts = version.Split('.');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out var major) || major < 0) return false;
        var minorStr = parts[1].Split('-', '+')[0];
        return int.TryParse(minorStr, out var minor) && minor >= 0;
    }

    // Parses "{PackageId}.{Version}.nupkg" → (packageId, version).
    // Finds the first "." followed by a digit that starts the version segment.
    private static bool TryParseNupkgName(string fileName, out string packageId, out string version)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName); // strip .nupkg
        // Find the first '.' followed by a digit — that is where the version starts
        for (var i = 0; i < baseName.Length - 1; i++)
        {
            if (baseName[i] == '.' && char.IsDigit(baseName[i + 1]))
            {
                packageId = baseName[..i];
                version = baseName[(i + 1)..];
                return !string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(version);
            }
        }
        packageId = string.Empty;
        version = string.Empty;
        return false;
    }
}
