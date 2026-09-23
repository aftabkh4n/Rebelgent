using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Process;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.Orchestration.Tests;

public class GitWorkspaceManagerCreateTests
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

    private static GitWorkspaceManager BuildManager(FakeProcessRunner fake, string rootPath) =>
        new(fake, Microsoft.Extensions.Options.Options.Create(new WorkspaceOptions { RootPath = rootPath }),
            NullLogger<GitWorkspaceManager>.Instance);

    // Successful path: git-dir OK, status clean, fetch OK, rev-parse origin/main OK, worktree OK
    private static void EnqueueSuccessPath(FakeProcessRunner fake)
    {
        fake.Enqueue(Ok(".git"));         // git rev-parse --git-dir
        fake.Enqueue(Ok(""));             // git status --porcelain (clean)
        fake.Enqueue(Ok());               // git fetch origin
        fake.Enqueue(Ok("abc1234\n"));    // git rev-parse --verify origin/main
        // git worktree add → default Ok from queue-empty path
    }

    // ── fetch behaviour ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_FetchesFromRemoteBeforeCreatingWorktree()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            await manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None);

            var fetchIdx = fake.Calls.FindIndex(c => c.Arguments.Contains("fetch"));
            var worktreeIdx = fake.Calls.FindIndex(c => c.Arguments.Contains("worktree"));
            Assert.True(fetchIdx >= 0, "Expected a git fetch call");
            Assert.True(worktreeIdx >= 0, "Expected a git worktree add call");
            Assert.True(fetchIdx < worktreeIdx, "git fetch must occur before git worktree add");
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public async Task CreateAsync_FetchesConfiguredRemoteName()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            fake.Enqueue(Ok(".git"));
            fake.Enqueue(Ok(""));
            fake.Enqueue(Ok());                // fetch
            fake.Enqueue(Ok("sha\n"));         // rev-parse upstream/main
            // worktree → default Ok
            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "upstream" };

            await manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None);

            var fetchCall = fake.Calls.First(c => c.Arguments.Contains("fetch"));
            Assert.Contains("upstream", fetchCall.Arguments);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public async Task CreateForRetryAsync_RemovesStaleWorktreeAndRecreatesBranchFromRemote()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        var taskId = Guid.NewGuid();
        var shortId = taskId.ToString("N")[..8];
        var workspacePath = Path.Combine(root, $"sandbox-{shortId}-developer");
        Directory.CreateDirectory(workspacePath);
        try
        {
            EnqueueSuccessPath(fake); // source validation must precede destructive cleanup
            fake.Enqueue(Ok()); // branch exists
            fake.Enqueue(Ok()); // branch is ancestor of remote default
            fake.Enqueue(Ok()); // worktree remove
            fake.Enqueue(Ok()); // worktree prune
            fake.Enqueue(Ok()); // branch -D
            fake.Enqueue(Ok(".git")); // CreateAsync git-dir
            fake.Enqueue(Ok("")); // status clean
            fake.Enqueue(Ok()); // fetch
            fake.Enqueue(Ok("sha\n")); // remote branch exists

            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            var workspace = await manager.CreateForRetryAsync(project, taskId, CancellationToken.None);

            Assert.Equal($"rebelgent/task-{shortId}", workspace.BranchName);
            Assert.Contains(fake.Calls, c => c.Arguments.SequenceEqual(["worktree", "remove", "--force", workspacePath]));
            Assert.Contains(fake.Calls, c => c.Arguments.SequenceEqual(["branch", "-D", $"rebelgent/task-{shortId}"]));
            var add = fake.Calls.Single(c => c.Arguments.Contains("add"));
            Assert.Contains("origin/main", add.Arguments);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public async Task Retry_UnrecordedCommits_RefusesBeforeRemovingWorkspaceOrBranch()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            EnqueueSuccessPath(fake);
            fake.Enqueue(Ok()); // branch exists
            fake.Enqueue(Fail()); // branch has commits absent from remote default
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo };
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildManager(fake, root).CreateForRetryAsync(project, Guid.NewGuid()));
            Assert.Contains("commits", error.Message);
            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("remove") || c.Arguments.Contains("-D"));
        }
        finally { Directory.Delete(root, true); Directory.Delete(repo, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retry_DirtySourceOrFetchFailure_DoesNotRemoveStaleWorkspace(bool dirty)
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        var id = Guid.NewGuid();
        var stale = Path.Combine(root, $"sandbox-{id.ToString("N")[..8]}-developer");
        Directory.CreateDirectory(stale);
        try
        {
            fake.Enqueue(Ok(".git"));
            fake.Enqueue(Ok(dirty ? " M modified.cs" : ""));
            if (!dirty) fake.Enqueue(Fail("fetch failed"));
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo };
            await Assert.ThrowsAsync<InvalidOperationException>(() => BuildManager(fake, root).CreateForRetryAsync(project, id));
            Assert.True(Directory.Exists(stale));
            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("remove") || c.Arguments.Contains("-D") || c.Arguments.Contains("add"));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public async Task CreateAsync_FetchFails_Throws()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            fake.Enqueue(Ok(".git"));           // git-dir
            fake.Enqueue(Ok(""));               // status clean
            fake.Enqueue(Fail("network error")); // fetch fails

            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    // ── remote ref as base ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_UsesRemoteRefAsWorktreeBase()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            await manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None);

            var worktreeCall = fake.Calls.First(c => c.Arguments.Contains("worktree") && c.Arguments.Contains("add"));
            // The base must be the remote ref, not the bare branch name
            Assert.Contains("origin/main", worktreeCall.Arguments);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public async Task CreateAsync_RemoteBranchMissing_Throws()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            fake.Enqueue(Ok(".git"));           // git-dir
            fake.Enqueue(Ok(""));               // status clean
            fake.Enqueue(Ok());                 // fetch
            fake.Enqueue(Fail("unknown ref"));  // rev-parse --verify origin/main fails

            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    // ── local main not touched ───────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DoesNotCheckoutOrPullOrResetLocalBranch()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            await manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None);

            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("checkout"));
            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("pull"));
            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("reset"));
            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("merge"));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public async Task CreateAsync_DoesNotVerifyLocalDefaultBranch()
    {
        // Local main may be stale or absent; only the fetched remote ref is used.
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            await manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None);

            // rev-parse calls must not use the bare local branch name as the ref to verify —
            // only the remote-qualified ref (origin/main) is acceptable.
            var revParseCalls = fake.Calls.Where(c => c.Arguments.Contains("rev-parse")).ToList();
            foreach (var call in revParseCalls)
            {
                var args = call.Arguments.ToList();
                var verifyIdx = args.IndexOf("--verify");
                if (verifyIdx >= 0 && verifyIdx + 1 < args.Count)
                    Assert.NotEqual("main", args[verifyIdx + 1]);
            }
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    // ── dirty repo rejected before fetch ────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DirtyRepo_RejectsBeforeFetch()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            fake.Enqueue(Ok(".git"));               // git-dir
            fake.Enqueue(Ok("M  src/Foo.cs\n"));   // status — dirty

            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None));

            // Dirty check fired before any network operation
            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("fetch"));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public async Task CreateAsync_DirtyRepo_DoesNotCreateWorktree()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            fake.Enqueue(Ok(".git"));
            fake.Enqueue(Ok("?? newfile.cs\n"));   // untracked — still dirty

            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None));

            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("worktree"));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    // ── no force / no reset / no pull ───────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NoForceResetOrPullUsed()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            await manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None);

            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("--force") || c.Arguments.Contains("-f"));
            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("reset"));
            Assert.DoesNotContain(fake.Calls, c => c.Arguments.Contains("pull"));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }

    // ── stale local main is irrelevant ───────────────────────────────────────

    [Fact]
    public async Task CreateAsync_StaleLocalMain_DoesNotAffectTaskBranch()
    {
        // Even if local main has not been updated in weeks, the task branch
        // is created from the fetched remote ref, so the base is always current.
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);
            var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = repo, DefaultBranch = "main", RemoteName = "origin" };

            var info = await manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None);

            // The worktree is created from origin/main, not local main
            var worktreeCall = fake.Calls.First(c => c.Arguments.Contains("worktree") && c.Arguments.Contains("add"));
            var args = worktreeCall.Arguments.ToList();
            Assert.Contains("origin/main", args);
            // Local "main" must not appear as the base argument
            var addIdx = args.IndexOf("add");
            // Arguments after "add <path> -b <branch>" are the base ref — it must not be bare "main"
            Assert.DoesNotContain("main", args.Skip(addIdx + 1).Where(a => !a.StartsWith("rebelgent/") && !a.Contains("sandbox-")));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repo, true);
        }
    }
}
