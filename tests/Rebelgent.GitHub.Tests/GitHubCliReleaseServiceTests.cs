using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.GitHub.Release;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.GitHub.Tests;

public class GitHubCliReleaseServiceTests
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

    private static GitHubCliReleaseService BuildService(FakeProcessRunner fake) =>
        new(fake, NullLogger<GitHubCliReleaseService>.Instance);

    private static CreateReleaseRequest MakeRequest(string tag = "v1.0.0") => new()
    {
        RepositoryPath = @"C:\repos\sandbox",
        TagName = tag,
        Title = "Release 1.0.0",
        Notes = "## What's Changed\n- Added feature",
        TargetCommitSha = "abc123def456abc123def456abc123def456abc1",
        GitHubRepository = "myorg/myrepo"
    };

    // ── ValidateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_GhAvailableAndAuthenticated_ReturnsReady()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("gh version 2.0.0"));
        fake.Enqueue(Ok("Logged in to github.com"));

        var result = await BuildService(fake).ValidateAsync(CancellationToken.None);

        Assert.True(result.IsReady);
    }

    [Fact]
    public async Task ValidateAsync_GhNotInstalled_ReturnsNotReady()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("gh: command not found"));

        var result = await BuildService(fake).ValidateAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Contains("not available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_GhNotAuthenticated_ReturnsNotReady()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("gh version 2.0.0"));
        fake.Enqueue(Fail("You are not logged in"));

        var result = await BuildService(fake).ValidateAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Contains("not authenticated", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── TagExistsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task TagExistsAsync_ReleaseViewSucceeds_ReturnsTrue()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("v1.0.0"));

        var result = await BuildService(fake).TagExistsAsync("v1.0.0", @"C:\repos\sandbox", null, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task TagExistsAsync_ReleaseViewFails_ReturnsFalse()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("release not found"));

        var result = await BuildService(fake).TagExistsAsync("v1.0.0", @"C:\repos\sandbox", null, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TagExistsAsync_WithGitHubRepository_PassesRepoFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());

        await BuildService(fake).TagExistsAsync("v1.0.0", @"C:\repos\sandbox", "myorg/myrepo", CancellationToken.None);

        var call = fake.Calls[0];
        Assert.Contains("--repo", call.Arguments);
        Assert.Contains("myorg/myrepo", call.Arguments);
    }

    [Fact]
    public async Task TagExistsAsync_WithoutGitHubRepository_DoesNotPassRepoFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());

        await BuildService(fake).TagExistsAsync("v1.0.0", @"C:\repos\sandbox", null, CancellationToken.None);

        var call = fake.Calls[0];
        Assert.DoesNotContain("--repo", call.Arguments);
    }

    // ── CreateReleaseAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateReleaseAsync_Success_ReturnsUrlFromStdout()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("https://github.com/myorg/myrepo/releases/tag/v1.0.0\n"));

        var result = await BuildService(fake).CreateReleaseAsync(MakeRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("https://github.com/myorg/myrepo/releases/tag/v1.0.0", result.ReleaseUrl);
    }

    [Fact]
    public async Task CreateReleaseAsync_GhFails_ReturnsFail()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("already exists"));

        var result = await BuildService(fake).CreateReleaseAsync(MakeRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CreateReleaseAsync_PassesTagTitleNotesTargetToArguments()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("https://github.com/myorg/myrepo/releases/tag/v1.0.0"));

        var request = MakeRequest("v2.3.4");
        await BuildService(fake).CreateReleaseAsync(request, CancellationToken.None);

        var call = fake.Calls[0];
        Assert.Contains("v2.3.4", call.Arguments);
        Assert.Contains("--title", call.Arguments);
        Assert.Contains("--notes", call.Arguments);
        Assert.Contains("--target", call.Arguments);
        Assert.Contains(request.TargetCommitSha, call.Arguments);
    }

    [Fact]
    public async Task CreateReleaseAsync_NoForceFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("https://github.com/myorg/myrepo/releases/tag/v1.0.0"));

        await BuildService(fake).CreateReleaseAsync(MakeRequest(), CancellationToken.None);

        var call = fake.Calls[0];
        Assert.DoesNotContain("--force", call.Arguments);
        Assert.DoesNotContain("-f", call.Arguments);
    }

    [Fact]
    public async Task CreateReleaseAsync_WithGitHubRepository_PassesRepoFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("https://github.com/myorg/myrepo/releases/tag/v1.0.0"));

        var request = MakeRequest() with { GitHubRepository = "myorg/myrepo" };
        await BuildService(fake).CreateReleaseAsync(request, CancellationToken.None);

        var call = fake.Calls[0];
        Assert.Contains("--repo", call.Arguments);
        Assert.Contains("myorg/myrepo", call.Arguments);
    }

    [Fact]
    public async Task CreateReleaseAsync_WithoutGitHubRepository_DoesNotPassRepoFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("https://github.com/myorg/myrepo/releases/tag/v1.0.0"));

        var request = MakeRequest() with { GitHubRepository = null };
        await BuildService(fake).CreateReleaseAsync(request, CancellationToken.None);

        var call = fake.Calls[0];
        Assert.DoesNotContain("--repo", call.Arguments);
    }

    [Fact]
    public async Task CreateReleaseAsync_UsesGhReleaseCreate()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("https://github.com/myorg/myrepo/releases/tag/v1.0.0"));

        await BuildService(fake).CreateReleaseAsync(MakeRequest(), CancellationToken.None);

        var call = fake.Calls[0];
        Assert.Equal("gh", call.FileName);
        Assert.Contains("release", call.Arguments);
        Assert.Contains("create", call.Arguments);
    }

    // ── GetLatestReleaseVersionAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetLatestReleaseVersionAsync_MultipleReleases_ReturnsHighest()
    {
        var json = """[{"tagName":"v1.0.0"},{"tagName":"v1.2.3"},{"tagName":"v1.1.0"}]""";
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok(json));

        var result = await BuildService(fake).GetLatestReleaseVersionAsync(@"C:\repos\sandbox", null, CancellationToken.None);

        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task GetLatestReleaseVersionAsync_MixedMalformedTags_IgnoresMalformed()
    {
        var json = """[{"tagName":"v1.0.0"},{"tagName":"latest"},{"tagName":"not-valid"},{"tagName":"v0.9.9"}]""";
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok(json));

        var result = await BuildService(fake).GetLatestReleaseVersionAsync(@"C:\repos\sandbox", null, CancellationToken.None);

        Assert.Equal("1.0.0", result);
    }

    [Fact]
    public async Task GetLatestReleaseVersionAsync_GhFails_ReturnsNull()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("HTTP 403: Forbidden"));

        var result = await BuildService(fake).GetLatestReleaseVersionAsync(@"C:\repos\sandbox", null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestReleaseVersionAsync_EmptyJson_ReturnsNull()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("[]"));

        var result = await BuildService(fake).GetLatestReleaseVersionAsync(@"C:\repos\sandbox", null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestReleaseVersionAsync_WithGitHubRepository_PassesRepoFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("""[{"tagName":"v1.0.0"}]"""));

        await BuildService(fake).GetLatestReleaseVersionAsync(@"C:\repos\sandbox", "myorg/myrepo", CancellationToken.None);

        var call = fake.Calls[0];
        Assert.Contains("--repo", call.Arguments);
        Assert.Contains("myorg/myrepo", call.Arguments);
    }

    [Fact]
    public async Task GetLatestReleaseVersionAsync_WithoutGitHubRepository_NoRepoFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("""[{"tagName":"v1.0.0"}]"""));

        await BuildService(fake).GetLatestReleaseVersionAsync(@"C:\repos\sandbox", null, CancellationToken.None);

        var call = fake.Calls[0];
        Assert.DoesNotContain("--repo", call.Arguments);
    }

    // ── ParseLatestVersionFromJson (internal, tested directly) ───────────────

    [Fact]
    public void ParseLatestVersionFromJson_ValidJson_ReturnsHighest()
    {
        var json = """[{"tagName":"v1.0.0"},{"tagName":"v2.0.0"},{"tagName":"v1.5.0"}]""";
        Assert.Equal("2.0.0", GitHubCliReleaseService.ParseLatestVersionFromJson(json));
    }

    [Fact]
    public void ParseLatestVersionFromJson_Empty_ReturnsNull()
    {
        Assert.Null(GitHubCliReleaseService.ParseLatestVersionFromJson("[]"));
    }

    [Fact]
    public void ParseLatestVersionFromJson_NullOrWhitespace_ReturnsNull()
    {
        Assert.Null(GitHubCliReleaseService.ParseLatestVersionFromJson(null!));
        Assert.Null(GitHubCliReleaseService.ParseLatestVersionFromJson(""));
        Assert.Null(GitHubCliReleaseService.ParseLatestVersionFromJson("   "));
    }

    [Fact]
    public void ParseLatestVersionFromJson_MalformedJson_ReturnsNull()
    {
        Assert.Null(GitHubCliReleaseService.ParseLatestVersionFromJson("not-json"));
    }
}
