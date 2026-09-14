using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.GitHub;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.GitHub.Tests;

public class GitHubCliPullRequestServiceTests
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

    private static GitHubCliPullRequestService BuildService(FakeProcessRunner fake) =>
        new(fake, NullLogger<GitHubCliPullRequestService>.Instance);

    private static PushAndCreateRequest MakeRequest(string branch = "rebelgent/task-abc12345") => new()
    {
        RepositoryPath = @"C:\repos\sandbox",
        BranchName = branch,
        RemoteName = "origin",
        BaseBranch = "main",
        Title = "Add feature X",
        Body = "## Description\nFeature X implemented.",
        GitHubRepository = "myorg/myrepo"
    };

    // ── ValidateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_GhAvailableAndAuthenticated_ReturnsReady()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("gh version 2.0.0"));   // gh --version
        fake.Enqueue(Ok("Logged in to github.com")); // gh auth status

        var svc = BuildService(fake);
        var result = await svc.ValidateAsync(CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.Equal(2, fake.Calls.Count);
        Assert.Contains("--version", fake.Calls[0].Arguments);
        Assert.Contains("auth", fake.Calls[1].Arguments);
    }

    [Fact]
    public async Task ValidateAsync_GhNotInstalled_ReturnsNotReady()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("gh: command not found"));  // gh --version fails

        var svc = BuildService(fake);
        var result = await svc.ValidateAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Contains("not available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_GhNotAuthenticated_ReturnsNotReady()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("gh version 2.0.0"));     // gh --version succeeds
        fake.Enqueue(Fail("You are not logged in"));  // gh auth status fails

        var svc = BuildService(fake);
        var result = await svc.ValidateAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Contains("not authenticated", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── PushAndCreateAsync — push safety ────────────────────────────────────

    [Fact]
    public async Task PushAndCreate_ProtectedBranchMain_RefusesWithoutPushing()
    {
        var fake = new FakeProcessRunner();
        var svc = BuildService(fake);
        var request = MakeRequest(branch: "main");

        var result = await svc.PushAndCreateAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(fake.Calls);
        Assert.Contains("protected branch", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PushAndCreate_ProtectedBranchMaster_RefusesWithoutPushing()
    {
        var fake = new FakeProcessRunner();
        var svc = BuildService(fake);
        var request = MakeRequest(branch: "master");

        var result = await svc.PushAndCreateAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task PushAndCreate_NeverUsesForcePush()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());                                         // git push
        fake.Enqueue(Ok("https://github.com/org/repo/pull/42\n")); // gh pr create

        var svc = BuildService(fake);
        await svc.PushAndCreateAsync(MakeRequest(), CancellationToken.None);

        var pushCall = fake.Calls.First(c => c.FileName == "git" && c.Arguments.Contains("push"));
        Assert.DoesNotContain("--force", pushCall.Arguments);
        Assert.DoesNotContain("-f", pushCall.Arguments);
    }

    [Fact]
    public async Task PushAndCreate_PushesExactDeveloperBranch()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("https://github.com/org/repo/pull/42\n"));

        var svc = BuildService(fake);
        await svc.PushAndCreateAsync(MakeRequest("rebelgent/task-abc12345"), CancellationToken.None);

        var pushCall = fake.Calls.First(c => c.FileName == "git" && c.Arguments.Contains("push"));
        Assert.Contains("rebelgent/task-abc12345", pushCall.Arguments);
        Assert.Contains("origin", pushCall.Arguments);
    }

    [Fact]
    public async Task PushAndCreate_GitPushFails_ReturnsFailWithoutCreatingPr()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("Permission denied"));  // git push fails

        var svc = BuildService(fake);
        var result = await svc.PushAndCreateAsync(MakeRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("push failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task PushAndCreate_GhPrCreateFails_ReturnsFail()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());                         // git push succeeds
        fake.Enqueue(Fail("Already exists"));       // gh pr create fails

        var svc = BuildService(fake);
        var result = await svc.PushAndCreateAsync(MakeRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("pr create failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PushAndCreate_Success_ReturnsPrNumberAndUrl()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("https://github.com/myorg/myrepo/pull/42\n"));

        var svc = BuildService(fake);
        var result = await svc.PushAndCreateAsync(MakeRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.PullRequestNumber);
        Assert.Equal("https://github.com/myorg/myrepo/pull/42", result.PullRequestUrl);
    }

    [Fact]
    public async Task PushAndCreate_UsesCorrectBaseBranch()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("https://github.com/org/repo/pull/1\n"));

        var svc = BuildService(fake);
        var request = MakeRequest() with { BaseBranch = "develop" };
        await svc.PushAndCreateAsync(request, CancellationToken.None);

        var prCall = fake.Calls.First(c => c.FileName == "gh" && c.Arguments.Contains("pr"));
        var args = prCall.Arguments.ToList();
        var baseIndex = args.IndexOf("--base");
        Assert.True(baseIndex >= 0);
        Assert.Equal("develop", args[baseIndex + 1]);
    }

    [Fact]
    public async Task PushAndCreate_UsesTitleFromRequest()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("https://github.com/org/repo/pull/7\n"));

        var svc = BuildService(fake);
        var request = MakeRequest() with { Title = "My specific PR title" };
        await svc.PushAndCreateAsync(request, CancellationToken.None);

        var prCall = fake.Calls.First(c => c.FileName == "gh" && c.Arguments.Contains("pr"));
        var args = prCall.Arguments.ToList();
        var titleIndex = args.IndexOf("--title");
        Assert.True(titleIndex >= 0);
        Assert.Equal("My specific PR title", args[titleIndex + 1]);
    }

    [Fact]
    public async Task PushAndCreate_WithGitHubRepository_IncludesRepoArg()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("https://github.com/org/repo/pull/5\n"));

        var svc = BuildService(fake);
        var request = MakeRequest() with { GitHubRepository = "myorg/myrepo" };
        await svc.PushAndCreateAsync(request, CancellationToken.None);

        var prCall = fake.Calls.First(c => c.FileName == "gh" && c.Arguments.Contains("pr"));
        Assert.Contains("--repo", prCall.Arguments);
        var args = prCall.Arguments.ToList();
        var repoIdx = args.IndexOf("--repo");
        Assert.Equal("myorg/myrepo", args[repoIdx + 1]);
    }

    [Fact]
    public async Task PushAndCreate_WithoutGitHubRepository_OmitsRepoArg()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("https://github.com/org/repo/pull/5\n"));

        var svc = BuildService(fake);
        var request = MakeRequest() with { GitHubRepository = null };
        await svc.PushAndCreateAsync(request, CancellationToken.None);

        var prCall = fake.Calls.First(c => c.FileName == "gh" && c.Arguments.Contains("pr"));
        Assert.DoesNotContain("--repo", prCall.Arguments);
    }

    [Fact]
    public async Task PushAndCreate_NeverInvokesCmdOrPowershell()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("https://github.com/org/repo/pull/99\n"));

        var svc = BuildService(fake);
        await svc.PushAndCreateAsync(MakeRequest(), CancellationToken.None);

        Assert.DoesNotContain(fake.Calls, c =>
            c.FileName.Contains("cmd", StringComparison.OrdinalIgnoreCase) ||
            c.FileName.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
            c.FileName.Contains("bash", StringComparison.OrdinalIgnoreCase));
    }
}
