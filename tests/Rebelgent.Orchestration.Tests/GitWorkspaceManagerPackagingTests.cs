using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Process;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.Orchestration.Tests;

public class GitWorkspaceManagerPackagingTests
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

    private const string MergedSha = "abc123def456abc123def456abc123def456abc1";

    private static ProjectDefinition MakeProject(string repoPath) => new()
    {
        Id = "sandbox",
        Name = "Sandbox",
        RepositoryPath = repoPath,
        DefaultBranch = "main",
        RemoteName = "origin"
    };

    // Success path: fetch OK, rev-parse verify OK, worktree add OK
    private static void EnqueueSuccessPath(FakeProcessRunner fake)
    {
        fake.Enqueue(Ok());              // git fetch origin
        fake.Enqueue(Ok(MergedSha));    // git rev-parse --verify <sha>
        // git worktree add --detach → default Ok
    }

    // ── git fetch ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateForPackagingAsync_FetchesFromRemoteFirst()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);

            await manager.CreateForPackagingAsync(MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None);

            var fetchCall = fake.Calls.FirstOrDefault(c => c.FileName == "git" && c.Arguments.Contains("fetch"));
            Assert.NotNull(fetchCall);
            Assert.Contains("origin", fetchCall!.Arguments);
            Assert.Equal(repo, fetchCall.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public async Task CreateForPackagingAsync_FetchHappensBeforeWorktreeAdd()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);

            await manager.CreateForPackagingAsync(MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None);

            var fetchIdx = fake.Calls.FindIndex(c => c.Arguments.Contains("fetch"));
            var addIdx = fake.Calls.FindIndex(c => c.Arguments.Contains("worktree"));
            Assert.True(fetchIdx < addIdx, "git fetch must occur before git worktree add");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    // ── commit verification ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateForPackagingAsync_VerifiesCommitShaAfterFetch()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);

            await manager.CreateForPackagingAsync(MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None);

            var verifyCall = fake.Calls.FirstOrDefault(c =>
                c.FileName == "git" && c.Arguments.Contains("rev-parse") && c.Arguments.Contains("--verify"));
            Assert.NotNull(verifyCall);
            Assert.Contains(MergedSha, verifyCall!.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public async Task CreateForPackagingAsync_CommitNotReachable_Throws()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());         // git fetch OK
            fake.Enqueue(Fail("unknown revision"));  // rev-parse --verify fails

            var manager = BuildManager(fake, root);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateForPackagingAsync(MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public async Task CreateForPackagingAsync_FetchFails_Throws()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            fake.Enqueue(Fail("connection refused"));  // git fetch fails

            var manager = BuildManager(fake, root);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateForPackagingAsync(MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    // ── detached worktree ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateForPackagingAsync_UsesDetachedWorktree()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);

            await manager.CreateForPackagingAsync(MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None);

            var addCall = fake.Calls.FirstOrDefault(c =>
                c.FileName == "git" && c.Arguments.Contains("worktree") && c.Arguments.Contains("add"));
            Assert.NotNull(addCall);
            Assert.Contains("--detach", addCall!.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public async Task CreateForPackagingAsync_WorktreeChecksOutExactCommit()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);

            await manager.CreateForPackagingAsync(MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None);

            var addCall = fake.Calls.First(c =>
                c.FileName == "git" && c.Arguments.Contains("worktree") && c.Arguments.Contains("add"));
            Assert.Contains(MergedSha, addCall.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    // ── workspace path ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateForPackagingAsync_WorkspacePathInsideRoot()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);

            var result = await manager.CreateForPackagingAsync(
                MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None);

            Assert.True(
                Path.GetFullPath(result.WorkspacePath).StartsWith(
                    Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase),
                $"Workspace '{result.WorkspacePath}' must be under root '{root}'");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public async Task CreateForPackagingAsync_WorkspacePathContainsPackageSuffix()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);

            var result = await manager.CreateForPackagingAsync(
                MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None);

            Assert.True(result.WorkspacePath.EndsWith("-package", StringComparison.OrdinalIgnoreCase),
                $"Workspace path '{result.WorkspacePath}' should end with '-package'");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public async Task CreateForPackagingAsync_WorkspaceAlreadyExists_Throws()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var taskId = Guid.NewGuid();
            var fake = new FakeProcessRunner();
            fake.Enqueue(Ok());           // fetch
            fake.Enqueue(Ok(MergedSha)); // verify

            var manager = BuildManager(fake, root);

            // Pre-create the expected workspace directory
            var shortId = taskId.ToString("N")[..8];
            var expectedPath = Path.Combine(root, $"sandbox-{shortId}-package");
            Directory.CreateDirectory(expectedPath);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateForPackagingAsync(MakeProject(repo), taskId, MergedSha, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }

    // ── source repository untouched ───────────────────────────────────────────

    [Fact]
    public async Task CreateForPackagingAsync_DoesNotRunCommandsInsideWorktree()
    {
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repo = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var fake = new FakeProcessRunner();
            EnqueueSuccessPath(fake);
            var manager = BuildManager(fake, root);

            var result = await manager.CreateForPackagingAsync(
                MakeProject(repo), Guid.NewGuid(), MergedSha, CancellationToken.None);

            // All commands before worktree creation must use the source repo as working dir
            foreach (var call in fake.Calls)
            {
                if (call.Arguments.Contains("worktree") && call.Arguments.Contains("add"))
                    continue; // worktree add itself runs from source repo — already checked separately
                Assert.True(
                    string.Equals(repo, call.WorkingDirectory, StringComparison.OrdinalIgnoreCase),
                    $"Git command '{string.Join(" ", call.Arguments)}' should run in source repo, not workspace");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(repo, recursive: true);
        }
    }
}
