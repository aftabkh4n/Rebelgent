using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Process;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.Orchestration.Tests;

public class GitWorkspaceManagerCommitTests
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

    private static GitWorkspaceManager BuildManager(FakeProcessRunner fake) =>
        new(fake, Microsoft.Extensions.Options.Options.Create(new WorkspaceOptions { RootPath = @"D:\Workspaces" }),
            NullLogger<GitWorkspaceManager>.Instance);

    private static ProcessResult Ok(string stdout = "") => new() { Success = true, ExitCode = 0, StandardOutput = stdout };
    private static ProcessResult Fail(string stderr = "error") => new() { Success = false, ExitCode = 1, StandardError = stderr };

    // ── CommitAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CommitAsync_WithChanges_CallsGitAddThenCommitThenRevParse()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());                             // git add --all
        fake.Enqueue(Ok("M  src/Foo.cs\n"));            // git status --porcelain (has changes)
        fake.Enqueue(Ok());                             // git commit
        fake.Enqueue(Ok("abc1234567890abcdef\n"));      // git rev-parse HEAD

        var manager = BuildManager(fake);
        var sha = await manager.CommitAsync(@"C:\ws\repo", "rebelgent: implement task abc12345", CancellationToken.None);

        Assert.Equal(4, fake.Calls.Count);
        Assert.Equal("git", fake.Calls[0].FileName);
        Assert.Contains("add", fake.Calls[0].Arguments);
        Assert.Contains("--all", fake.Calls[0].Arguments);
        Assert.Contains("commit", fake.Calls[2].Arguments);
        Assert.Contains("rev-parse", fake.Calls[3].Arguments);
        Assert.Equal("abc1234567890abcdef", sha);
    }

    [Fact]
    public async Task CommitAsync_WithChanges_UsesCorrectCommitMessage()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("M  src/Foo.cs\n"));
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("deadbeef\n"));

        var manager = BuildManager(fake);
        await manager.CommitAsync(@"C:\ws\repo", "rebelgent: implement task abc12345", CancellationToken.None);

        var commitCall = fake.Calls.First(c => c.Arguments.Contains("commit"));
        var argList = commitCall.Arguments.ToList();
        var msgIndex = argList.IndexOf("-m");
        Assert.True(msgIndex >= 0, "Expected -m flag in commit arguments");
        Assert.Equal("rebelgent: implement task abc12345", argList[msgIndex + 1]);
    }

    [Fact]
    public async Task CommitAsync_WithChanges_SetsRebelgentUserIdentity()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("M  src/Foo.cs\n"));
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("deadbeef\n"));

        var manager = BuildManager(fake);
        await manager.CommitAsync(@"C:\ws\repo", "rebelgent: implement task abc12345", CancellationToken.None);

        var commitCall = fake.Calls.First(c => c.Arguments.Contains("commit"));
        var args = string.Join(" ", commitCall.Arguments);
        Assert.Contains("user.name=Rebelgent", args);
        Assert.Contains("user.email=rebelgent@noreply.local", args);
    }

    [Fact]
    public async Task CommitAsync_DoesNotCallGitPush()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("M  src/Foo.cs\n"));
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("deadbeef\n"));

        var manager = BuildManager(fake);
        await manager.CommitAsync(@"C:\ws\repo", "rebelgent: implement task abc12345", CancellationToken.None);

        Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("push"));
    }

    [Fact]
    public async Task CommitAsync_DoesNotCallGitMerge()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("M  src/Foo.cs\n"));
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("deadbeef\n"));

        var manager = BuildManager(fake);
        await manager.CommitAsync(@"C:\ws\repo", "rebelgent: implement task abc12345", CancellationToken.None);

        Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("merge"));
    }

    [Fact]
    public async Task CommitAsync_NothingToCommit_ReturnsCurrentHeadSha()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());                         // git add --all
        fake.Enqueue(Ok(string.Empty));             // git status --porcelain (clean — nothing staged)
        fake.Enqueue(Ok("originalsha123\n"));       // git rev-parse HEAD

        var manager = BuildManager(fake);
        var sha = await manager.CommitAsync(@"C:\ws\repo", "rebelgent: implement task abc12345", CancellationToken.None);

        Assert.Equal("originalsha123", sha);
        // Should NOT have called git commit
        Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("commit"));
    }

    [Fact]
    public async Task CommitAsync_GitAddFails_Throws()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Fail("permission denied"));    // git add fails

        var manager = BuildManager(fake);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CommitAsync(@"C:\ws\repo", "rebelgent: implement task abc12345", CancellationToken.None));
    }

    [Fact]
    public async Task CommitAsync_GitCommitFails_Throws()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());                         // git add --all
        fake.Enqueue(Ok("M  src/Foo.cs\n"));        // git status (has changes)
        fake.Enqueue(Fail("commit error"));         // git commit fails

        var manager = BuildManager(fake);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CommitAsync(@"C:\ws\repo", "rebelgent: implement task abc12345", CancellationToken.None));
    }

    [Fact]
    public async Task CommitAsync_ReturnsTrimedSha()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("M  src/Foo.cs\n"));
        fake.Enqueue(Ok());
        fake.Enqueue(Ok("  abc123def456  \n"));     // SHA with whitespace

        var manager = BuildManager(fake);
        var sha = await manager.CommitAsync(@"C:\ws\repo", "rebelgent: implement task abc12345", CancellationToken.None);

        Assert.Equal("abc123def456", sha);
    }

    // ── CreateFromBranchAsync — commit SHA passthrough ───────────────────────

    [Fact]
    public async Task CreateFromBranchAsync_WithCommitSha_UsesCommitShaInGitCommand()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repoDir = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var manager = new GitWorkspaceManager(fake,
                Microsoft.Extensions.Options.Options.Create(new WorkspaceOptions { RootPath = root }),
                NullLogger<GitWorkspaceManager>.Instance);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repoDir, DefaultBranch = "main" };

            await manager.CreateFromBranchAsync(project, "rebelgent/task-abc", "qa", "deadbeef1234", CancellationToken.None);

            var worktreeCall = fake.Calls.Single();
            Assert.Contains("deadbeef1234", worktreeCall.Arguments);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repoDir, true);
        }
    }

    [Fact]
    public async Task CreateFromBranchAsync_WithoutCommitSha_UsesBranchName()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(Ok());
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repoDir = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var manager = new GitWorkspaceManager(fake,
                Microsoft.Extensions.Options.Options.Create(new WorkspaceOptions { RootPath = root }),
                NullLogger<GitWorkspaceManager>.Instance);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repoDir, DefaultBranch = "main" };

            await manager.CreateFromBranchAsync(project, "rebelgent/task-abc", "qa", null, CancellationToken.None);

            var worktreeCall = fake.Calls.Single();
            Assert.Contains("rebelgent/task-abc", worktreeCall.Arguments);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repoDir, true);
        }
    }
}
