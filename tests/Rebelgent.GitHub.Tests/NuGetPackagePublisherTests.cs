using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rebelgent.Core.Publishing;
using Rebelgent.GitHub.Package;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.GitHub.Tests;

public class NuGetPackagePublisherTests
{
    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new();
        public List<ProcessRunOptions> Calls { get; } = [];

        public void Enqueue(ProcessResult result) => _results.Enqueue(result);

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken = default)
        {
            Calls.Add(options);
            var result = _results.Count > 0
                ? _results.Dequeue()
                : new ProcessResult { Success = true, ExitCode = 0 };
            return Task.FromResult(result);
        }
    }

    private static ProcessResult Ok(string stdout = "") => new() { Success = true, ExitCode = 0, StandardOutput = stdout };
    private static ProcessResult Fail(string stderr = "error") => new() { Success = false, ExitCode = 1, StandardError = stderr };

    // A non-public, non-local HTTP feed used in tests that need an HTTP source without triggering the nuget.org block.
    private const string PrivateFeedUrl = "https://private-feed.example.com/v3/index.json";

    private static NuGetPackagePublisher BuildPublisher(
        FakeProcessRunner fake,
        string? apiKeyConfig = null,
        string? source = null,
        bool allowPublicPublish = false)
    {
        var configValues = new Dictionary<string, string?>();
        if (apiKeyConfig is not null)
            configValues["NuGet:ApiKey"] = apiKeyConfig;

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var options = Microsoft.Extensions.Options.Options.Create(new NuGetOptions
        {
            Source = source ?? string.Empty,  // no implicit default — tests that need a source must pass one
            AllowPublicPublish = allowPublicPublish
        });
        return new NuGetPackagePublisher(fake, options, config, NullLogger<NuGetPackagePublisher>.Instance);
    }

    // Creates a temp repo root and a packages subdirectory inside it.
    // The repo root is used as RepositoryPath; the output dir is a subdir of it.
    // Caller is responsible for deleting repoPath when done.
    private static (string repoPath, string outputDir) CreateTestDirs()
    {
        var repoPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var outputDir = Path.Combine(repoPath, "artifacts", "packages");
        Directory.CreateDirectory(outputDir);
        return (repoPath, outputDir);
    }

    // ── PrepareAsync — process args ──────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_UsesDotnetPack()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok("Pack succeeded."));

            await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
            });

            Assert.Equal("dotnet", fake.Calls[0].FileName);
            Assert.Contains("pack", fake.Calls[0].Arguments);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_WithProjectFilePath_IncludesProjectInArgs()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());
            var projectFile = Path.Combine(repoPath, "src", "MyLib", "MyLib.csproj");

            await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                ProjectFilePath = projectFile,
                OutputDirectory = outputDir
            });

            Assert.Contains(projectFile, fake.Calls[0].Arguments);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_DotnetPackFails_ReturnsFailResult()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            var fake = new FakeProcessRunner();
            fake.Enqueue(Fail("Build error"));

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
            });

            Assert.False(result.Succeeded);
            Assert.Contains("dotnet pack failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_NoNupkgProduced_ReturnsFailResult()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok()); // pack succeeds but no .nupkg written

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
            });

            Assert.False(result.Succeeded);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_WithExpectedVersion_ParsesPackageIdAndVersion()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "My.Awesome.Lib.2.3.4.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                ExpectedVersion = "2.3.4"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("My.Awesome.Lib", result.PackageId);
            Assert.Equal("2.3.4", result.PackageVersion);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_WithExpectedVersion_WrongVersion_ReturnsFail()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                ExpectedVersion = "2.0.0" // wrong
            });

            Assert.False(result.Succeeded);
            Assert.Contains("mismatch", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_WithoutExpectedVersion_ParsesFromFilename()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "SomePackage.3.1.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
            });

            Assert.True(result.Succeeded);
            Assert.Equal("SomePackage", result.PackageId);
            Assert.Equal("3.1.0", result.PackageVersion);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_PassesOutputDirAndConfigurationToArgs()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                Configuration = "Release"
            });

            var args = fake.Calls[0].Arguments;
            Assert.Contains("--configuration", args);
            Assert.Contains("Release", args);
            Assert.Contains("--output", args);
            Assert.Contains(outputDir, args);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    // ── PrepareAsync — package written to explicit output directory ──────────

    [Fact]
    public async Task PrepareAsync_OutputDirectoryPassedToPackCommand()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
            });

            var args = fake.Calls[0].Arguments;
            var outputIdx = args.ToList().IndexOf("--output");
            Assert.NotEqual(-1, outputIdx);
            Assert.Equal(outputDir, args[outputIdx + 1]);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    // ── PrepareAsync — symbols package ignored ───────────────────────────────

    [Fact]
    public async Task PrepareAsync_SymbolsOnlyNupkg_ReturnsFail()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            // Only a .symbols.nupkg exists — no regular package
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.symbols.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
            });

            Assert.False(result.Succeeded);
            Assert.Contains("no .nupkg", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_NormalAndSymbolsNupkgBothPresent_ReturnsNormalPackage()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []);
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.symbols.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
            });

            Assert.True(result.Succeeded);
            Assert.Equal("MyLib", result.PackageId);
            Assert.Equal("1.0.0", result.PackageVersion);
            Assert.EndsWith("MyLib.1.0.0.nupkg", result.PackagePath, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    // ── PrepareAsync — multiple package handling ─────────────────────────────

    [Fact]
    public async Task PrepareAsync_MultipleNupkgs_NoVersionSpecified_ReturnsFail()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []);
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.Extensions.1.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
            });

            Assert.False(result.Succeeded);
            Assert.Contains("multiple", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_MultipleNupkgs_WithExpectedVersion_Disambiguates()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []);
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.2.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                ExpectedVersion = "1.0.0"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("MyLib", result.PackageId);
            Assert.Equal("1.0.0", result.PackageVersion);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_MultipleNupkgsSameVersion_WithExpectedPackageId_Disambiguates()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.Core.1.0.0.nupkg"), []);
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.Extensions.1.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                ExpectedVersion = "1.0.0",
                ExpectedPackageId = "MyLib.Core"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("MyLib.Core", result.PackageId);
            Assert.Equal("1.0.0", result.PackageVersion);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_MultipleNupkgsSameVersion_NoPackageId_ReturnsFail()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.Core.1.0.0.nupkg"), []);
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.Extensions.1.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                ExpectedVersion = "1.0.0"
                // No ExpectedPackageId — ambiguous
            });

            Assert.False(result.Succeeded);
            Assert.Contains("NuGetPackageId", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    // ── PrepareAsync — PackageVersion MSBuild injection ──────────────────────

    [Fact]
    public async Task PrepareAsync_WithPackageVersion_1_1_0_InjectsVersionMsbuildProperties()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.1.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                PackageVersion = "1.1.0"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("1.1.0", result.PackageVersion);
            var args = fake.Calls[0].Arguments;
            Assert.Contains("/p:PackageVersion=1.1.0", args);
            Assert.Contains("/p:Version=1.1.0", args);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_WithPackageVersion_2_0_0_InjectsVersionMsbuildProperties()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.2.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                PackageVersion = "2.0.0"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("2.0.0", result.PackageVersion);
            var args = fake.Calls[0].Arguments;
            Assert.Contains("/p:PackageVersion=2.0.0", args);
            Assert.Contains("/p:Version=2.0.0", args);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_PackageVersionOverridesCsprojDefault()
    {
        // Without /p:PackageVersion, dotnet would produce MyLib.1.0.0.nupkg (csproj default).
        // With /p:PackageVersion=1.1.0, it produces MyLib.1.1.0.nupkg.
        // The test verifies both the injected args and that the result carries 1.1.0, not 1.0.0.
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.1.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                PackageVersion = "1.1.0",
                ExpectedVersion = "1.1.0"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("MyLib", result.PackageId);
            Assert.Equal("1.1.0", result.PackageVersion);
            Assert.Contains("/p:PackageVersion=1.1.0", fake.Calls[0].Arguments);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_WithoutPackageVersion_DoesNotInjectVersionArgs()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []);
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
                // No PackageVersion — csproj default is used
            });

            var args = fake.Calls[0].Arguments;
            Assert.DoesNotContain(args, a => a.StartsWith("/p:PackageVersion", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(args, a => a.StartsWith("/p:Version", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_MalformedPackageVersion_ReturnsFail_ProcessNotCalled()
    {
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            var fake = new FakeProcessRunner();

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                PackageVersion = "not-a-version"
            });

            Assert.False(result.Succeeded);
            Assert.Contains("PackageVersion", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(fake.Calls); // must reject before calling the process runner
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    [Fact]
    public async Task PrepareAsync_PackageVersionMismatch_ReturnsFail()
    {
        // dotnet pack was supposed to produce 1.1.0 via /p:PackageVersion, but the .nupkg on disk is 1.0.0.
        // This guards against pack silently ignoring the MSBuild property.
        var (repoPath, outputDir) = CreateTestDirs();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyLib.1.0.0.nupkg"), []); // wrong version
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());

            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir,
                PackageVersion = "1.1.0",
                ExpectedVersion = "1.1.0"
            });

            Assert.False(result.Succeeded);
            Assert.Contains("mismatch", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(repoPath, recursive: true); }
    }

    // ── PrepareAsync — path cannot escape workspace ──────────────────────────

    [Fact]
    public async Task PrepareAsync_OutputDirectoryOutsideRepository_ReturnsFail()
    {
        var fake = new FakeProcessRunner();
        // outputDir is not a subdirectory of repoPath
        var repoPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        try
        {
            var result = await BuildPublisher(fake).PrepareAsync(new PackagePrepareRequest
            {
                RepositoryPath = repoPath,
                OutputDirectory = outputDir
            });

            Assert.False(result.Succeeded);
            Assert.Contains("outside", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(fake.Calls); // process runner must not be called
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    // ── PublishAsync — source safety ─────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_SourceNotConfigured_ReturnsFail_ProcessNotCalled()
    {
        var fake = new FakeProcessRunner();
        var publisher = BuildPublisher(fake); // source defaults to empty

        var result = await publisher.PublishAsync(new PackagePublishRequest { PackagePath = @"C:\temp\lib.nupkg" });

        Assert.False(result.Succeeded);
        Assert.Contains("source is not configured", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task PublishAsync_NuGetOrgSource_BlockedByDefault()
    {
        var fake = new FakeProcessRunner();
        // AllowPublicPublish defaults to false
        var publisher = BuildPublisher(fake, apiKeyConfig: "key",
            source: "https://api.nuget.org/v3/index.json");

        var result = await publisher.PublishAsync(new PackagePublishRequest { PackagePath = @"C:\temp\lib.nupkg" });

        Assert.False(result.Succeeded);
        Assert.Contains("nuget.org", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllowPublicPublish", result.ErrorMessage);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task PublishAsync_NuGetOrgSource_AllowedWithAllowPublicPublishTrue()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("Your package was pushed."));
        var publisher = BuildPublisher(fake, apiKeyConfig: "key",
            source: "https://api.nuget.org/v3/index.json",
            allowPublicPublish: true);

        var result = await publisher.PublishAsync(new PackagePublishRequest { PackagePath = @"C:\temp\lib.nupkg" });

        Assert.True(result.Succeeded);
        Assert.Single(fake.Calls); // process was called
    }

    [Fact]
    public async Task PublishAsync_LocalFolderSource_AcceptedWithoutApiKey()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        // No API key configured — local sources must not require one
        var publisher = BuildPublisher(fake, apiKeyConfig: null,
            source: @"D:\LocalFeed");

        var result = await publisher.PublishAsync(new PackagePublishRequest { PackagePath = @"C:\temp\lib.nupkg" });

        Assert.True(result.Succeeded);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task PublishAsync_LocalFolderSource_DoesNotIncludeApiKeyInArgs()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        var publisher = BuildPublisher(fake, apiKeyConfig: null,
            source: @"D:\LocalFeed");

        await publisher.PublishAsync(new PackagePublishRequest { PackagePath = @"C:\temp\lib.nupkg" });

        var args = fake.Calls[0].Arguments;
        Assert.DoesNotContain("--api-key", args);
        Assert.Empty(fake.Calls[0].SecretArgumentIndices);
    }

    [Fact]
    public async Task PublishAsync_ConfiguredSourcePassedToArgs()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        var publisher = BuildPublisher(fake, apiKeyConfig: "key", source: PrivateFeedUrl);

        await publisher.PublishAsync(new PackagePublishRequest { PackagePath = @"C:\temp\lib.nupkg" });

        var args = fake.Calls[0].Arguments;
        Assert.Contains("--source", args);
        Assert.Contains(PrivateFeedUrl, args);
    }

    // ── PublishAsync — API key required for HTTP feeds ────────────────────────

    [Fact]
    public async Task PublishAsync_NoApiKey_HttpSource_ReturnsFail_NeverCallsProcess()
    {
        var fake = new FakeProcessRunner();
        var publisher = BuildPublisher(fake, apiKeyConfig: null, source: PrivateFeedUrl);
        Environment.SetEnvironmentVariable("NUGET_API_KEY", null);

        var result = await publisher.PublishAsync(new PackagePublishRequest { PackagePath = @"C:\temp\lib.nupkg" });

        Assert.False(result.Succeeded);
        Assert.Contains("API key", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task PublishAsync_WithApiKeyFromConfig_CallsDotnetNugetPush()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("Your package was pushed."));
        var publisher = BuildPublisher(fake, apiKeyConfig: "test-api-key-12345", source: PrivateFeedUrl);

        var result = await publisher.PublishAsync(new PackagePublishRequest
        {
            PackagePath = @"C:\temp\MyLib.1.0.0.nupkg"
        });

        Assert.True(result.Succeeded);
        Assert.Single(fake.Calls);
        Assert.Equal("dotnet", fake.Calls[0].FileName);
        Assert.Contains("nuget", fake.Calls[0].Arguments);
        Assert.Contains("push", fake.Calls[0].Arguments);
    }

    [Fact]
    public async Task PublishAsync_ApiKeyIsInArguments_MaskedViaSecretArgumentIndices()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        var publisher = BuildPublisher(fake, apiKeyConfig: "super-secret-key", source: PrivateFeedUrl);

        await publisher.PublishAsync(new PackagePublishRequest
        {
            PackagePath = @"C:\temp\MyLib.1.0.0.nupkg"
        });

        var options = fake.Calls[0];
        // Key value must appear in args and be marked in SecretArgumentIndices
        var keyIdx = options.Arguments.ToList().IndexOf("super-secret-key");
        Assert.NotEqual(-1, keyIdx);
        Assert.Contains(keyIdx, options.SecretArgumentIndices);
    }

    [Fact]
    public async Task PublishAsync_NoPushWithoutApiKey_ZeroProcessCalls()
    {
        var fake = new FakeProcessRunner();
        var publisher = BuildPublisher(fake, apiKeyConfig: null, source: PrivateFeedUrl);
        Environment.SetEnvironmentVariable("NUGET_API_KEY", null);

        await publisher.PublishAsync(new PackagePublishRequest { PackagePath = @"C:\temp\lib.nupkg" });

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task PublishAsync_DotnetNugetPushFails_ReturnsFailResult()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("403 Forbidden"));
        var publisher = BuildPublisher(fake, apiKeyConfig: "valid-key", source: PrivateFeedUrl);

        var result = await publisher.PublishAsync(new PackagePublishRequest
        {
            PackagePath = @"C:\temp\MyLib.1.0.0.nupkg"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("push failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishAsync_DoesNotContainSkipDuplicate()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        var publisher = BuildPublisher(fake, apiKeyConfig: "key", source: PrivateFeedUrl);

        await publisher.PublishAsync(new PackagePublishRequest
        {
            PackagePath = @"C:\temp\MyLib.1.0.0.nupkg"
        });

        var args = fake.Calls[0].Arguments;
        Assert.DoesNotContain("--skip-duplicate", args);
    }

    [Fact]
    public async Task PublishAsync_DoesNotContainForceFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        var publisher = BuildPublisher(fake, apiKeyConfig: "key", source: PrivateFeedUrl);

        await publisher.PublishAsync(new PackagePublishRequest
        {
            PackagePath = @"C:\temp\MyLib.1.0.0.nupkg"
        });

        var args = fake.Calls[0].Arguments;
        Assert.DoesNotContain("--force", args);
        Assert.DoesNotContain("-f", args);
    }

    [Fact]
    public async Task PublishAsync_PassesPackagePathAsArgument()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        var publisher = BuildPublisher(fake, apiKeyConfig: "key", source: PrivateFeedUrl);
        const string packagePath = @"C:\temp\MyLib.1.0.0.nupkg";

        await publisher.PublishAsync(new PackagePublishRequest { PackagePath = packagePath });

        Assert.Contains(packagePath, fake.Calls[0].Arguments);
    }

    [Fact]
    public async Task PublishAsync_PassesSourceFromOptions()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        var publisher = BuildPublisher(fake, apiKeyConfig: "key", source: "https://my-feed.example.com/v3/index.json");

        await publisher.PublishAsync(new PackagePublishRequest { PackagePath = @"C:\temp\lib.nupkg" });

        var args = fake.Calls[0].Arguments;
        Assert.Contains("--source", args);
        Assert.Contains("https://my-feed.example.com/v3/index.json", args);
    }
}
