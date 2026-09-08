using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.GitHub;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.GitHub.Tests;

public class GitHubCliPullRequestMergeServiceTests
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

    private static GitHubCliPullRequestMergeService BuildService(FakeProcessRunner fake) =>
        new(fake, NullLogger<GitHubCliPullRequestMergeService>.Instance);

    private static MergeRequest MakeRequest(int prNumber = 42, string branch = "rebelgent/task-abc12345") => new()
    {
        RepositoryPath = @"C:\repos\sandbox",
        PullRequestNumber = prNumber,
        ExpectedHeadBranch = branch,
        ExpectedBaseBranch = "main",
        GitHubRepository = "myorg/myrepo"
    };

    // ── ValidateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_GhAvailableAndAuthenticated_ReturnsReady()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("gh version 2.0.0"));
        fake.Enqueue(Ok("Logged in to github.com"));

        var svc = BuildService(fake);
        var result = await svc.ValidateAsync(CancellationToken.None);

        Assert.True(result.IsReady);
    }

    [Fact]
    public async Task ValidateAsync_GhNotInstalled_ReturnsNotReady()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("gh: command not found"));

        var svc = BuildService(fake);
        var result = await svc.ValidateAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Contains("not available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_GhNotAuthenticated_ReturnsNotReady()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("gh version 2.0.0"));
        fake.Enqueue(Fail("You are not logged in"));

        var svc = BuildService(fake);
        var result = await svc.ValidateAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Contains("not authenticated", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── GetPrStateAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPrState_Success_ParsesFields()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("{\"headRefName\":\"rebelgent/task-abc12345\",\"baseRefName\":\"main\",\"state\":\"OPEN\",\"mergeable\":\"MERGEABLE\"}"));

        var svc = BuildService(fake);
        var result = await svc.GetPrStateAsync(42, @"C:\repos\sandbox", "myorg/myrepo", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("rebelgent/task-abc12345", result.HeadBranch);
        Assert.Equal("main", result.BaseBranch);
        Assert.Equal("OPEN", result.State);
        Assert.True(result.Mergeable);
    }

    [Fact]
    public async Task GetPrState_ConflictingMergeable_ReturnsFalseMergeable()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("{\"headRefName\":\"branch\",\"baseRefName\":\"main\",\"state\":\"OPEN\",\"mergeable\":\"CONFLICTING\"}"));

        var svc = BuildService(fake);
        var result = await svc.GetPrStateAsync(42, @"C:\repos\sandbox", null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Mergeable);
    }

    [Fact]
    public async Task GetPrState_GhFails_ReturnsFail()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("pull request not found"));

        var svc = BuildService(fake);
        var result = await svc.GetPrStateAsync(42, @"C:\repos\sandbox", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task GetPrState_IncludesRepoArgWhenProvided()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("{\"headRefName\":\"b\",\"baseRefName\":\"main\",\"state\":\"OPEN\",\"mergeable\":\"MERGEABLE\"}"));

        var svc = BuildService(fake);
        await svc.GetPrStateAsync(42, @"C:\repos\sandbox", "myorg/myrepo", CancellationToken.None);

        var call = fake.Calls[0];
        Assert.Contains("--repo", call.Arguments);
    }

    [Fact]
    public async Task GetPrState_OmitsRepoArgWhenNotProvided()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok("{\"headRefName\":\"b\",\"baseRefName\":\"main\",\"state\":\"OPEN\",\"mergeable\":\"MERGEABLE\"}"));

        var svc = BuildService(fake);
        await svc.GetPrStateAsync(42, @"C:\repos\sandbox", null, CancellationToken.None);

        var call = fake.Calls[0];
        Assert.DoesNotContain("--repo", call.Arguments);
    }

    // ── MergeAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_UsesSquashFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());    // gh pr merge
        fake.Enqueue(Ok("{\"mergeCommit\":{\"oid\":\"abc123\"}}")); // gh pr view mergeCommit

        var svc = BuildService(fake);
        await svc.MergeAsync(MakeRequest(), CancellationToken.None);

        var mergeCall = fake.Calls[0];
        Assert.Contains("--squash", mergeCall.Arguments);
    }

    [Fact]
    public async Task MergeAsync_NeverUsesAdminFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("{\"mergeCommit\":{\"oid\":\"abc123\"}}"));

        var svc = BuildService(fake);
        await svc.MergeAsync(MakeRequest(), CancellationToken.None);

        Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("--admin"));
    }

    [Fact]
    public async Task MergeAsync_NeverUsesForceFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("{\"mergeCommit\":{\"oid\":\"abc123\"}}"));

        var svc = BuildService(fake);
        await svc.MergeAsync(MakeRequest(), CancellationToken.None);

        Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("--force") || c.Arguments.Contains("-f"));
    }

    [Fact]
    public async Task MergeAsync_NeverDeletesBranch()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("{\"mergeCommit\":{\"oid\":\"abc123\"}}"));

        var svc = BuildService(fake);
        await svc.MergeAsync(MakeRequest(), CancellationToken.None);

        Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("--delete-branch"));
    }

    [Fact]
    public async Task MergeAsync_Success_ReturnsMergeMethodSquash()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("{\"mergeCommit\":{\"oid\":\"deadbeef\"}}"));

        var svc = BuildService(fake);
        var result = await svc.MergeAsync(MakeRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("squash", result.MergeMethod);
    }

    [Fact]
    public async Task MergeAsync_Success_ReturnsMergeCommitSha()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("{\"mergeCommit\":{\"oid\":\"deadbeefdeadbeef\"}}"));

        var svc = BuildService(fake);
        var result = await svc.MergeAsync(MakeRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("deadbeefdeadbeef", result.MergeCommitSha);
    }

    [Fact]
    public async Task MergeAsync_GhFails_ReturnsFail()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("merge failed: required status checks have not passed"));

        var svc = BuildService(fake);
        var result = await svc.MergeAsync(MakeRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("merge failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeAsync_IncludesRepoArgWhenProvided()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("{\"mergeCommit\":{\"oid\":\"abc\"}}"));

        var svc = BuildService(fake);
        await svc.MergeAsync(MakeRequest(), CancellationToken.None);

        var mergeCall = fake.Calls[0];
        Assert.Contains("--repo", mergeCall.Arguments);
    }

    [Fact]
    public async Task MergeAsync_NeverInvokesCmdOrPowershell()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("{\"mergeCommit\":{\"oid\":\"abc\"}}"));

        var svc = BuildService(fake);
        await svc.MergeAsync(MakeRequest(), CancellationToken.None);

        Assert.DoesNotContain(fake.Calls, c =>
            c.FileName.Contains("cmd", StringComparison.OrdinalIgnoreCase) ||
            c.FileName.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
            c.FileName.Contains("bash", StringComparison.OrdinalIgnoreCase));
    }
}
