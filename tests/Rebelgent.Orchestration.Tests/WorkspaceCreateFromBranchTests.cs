using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Process;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.Orchestration.Tests;

public class WorkspaceCreateFromBranchTests
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

    private static GitWorkspaceManager BuildManager(FakeProcessRunner fake, string rootPath) =>
        new(fake, Microsoft.Extensions.Options.Options.Create(new WorkspaceOptions { RootPath = rootPath }),
            NullLogger<GitWorkspaceManager>.Instance);

    private static ProjectDefinition MakeProject(string repoPath) => new()
    {
        Id = "sandbox",
        Name = "Sandbox",
        RepositoryPath = repoPath,
        DefaultBranch = "main"
    };

    [Fact]
    public async Task CreateFromBranchAsync_RepoDoesNotExist_Throws()
    {
        var fake = new FakeProcessRunner();
        var manager = BuildManager(fake, @"D:\Workspaces");
        var project = MakeProject(@"C:\does-not-exist\repo");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CreateFromBranchAsync(project, "rebelgent/task-abc", "qa", cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task CreateFromBranchAsync_InvalidRoleSuffix_Throws()
    {
        var fake = new FakeProcessRunner();
        var tempDir = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var manager = BuildManager(fake, @"D:\Workspaces");
            var project = MakeProject(tempDir);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                manager.CreateFromBranchAsync(project, "rebelgent/task-abc", "qa/../evil", cancellationToken: CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CreateFromBranchAsync_Success_UsesDetachedWorktreeAdd()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(new ProcessResult { Success = true, ExitCode = 0 });
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repoDir = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var manager = BuildManager(fake, root);
            var project = MakeProject(repoDir);

            var result = await manager.CreateFromBranchAsync(project, "rebelgent/task-abcd1234", "qa", cancellationToken: CancellationToken.None);

            Assert.Single(fake.Calls);
            Assert.Equal("git", fake.Calls[0].FileName);
            Assert.Contains("--detach", fake.Calls[0].Arguments);
            Assert.Equal("rebelgent/task-abcd1234", result.BranchName);
            Assert.StartsWith(root, result.WorkspacePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repoDir, true);
        }
    }

    [Fact]
    public async Task CreateFromBranchAsync_WorkspaceAlreadyExists_Throws()
    {
        var fake = new FakeProcessRunner();
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repoDir = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var manager = BuildManager(fake, root);
            var project = MakeProject(repoDir);

            // Compute expected path: project.Id + branchSlug + roleSuffix
            var branchSlug = "rebelgent-task-abcd";
            var expectedPath = Path.Combine(root, $"sandbox-{branchSlug}-qa");
            Directory.CreateDirectory(expectedPath);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateFromBranchAsync(project, "rebelgent/task-abcd", "qa", cancellationToken: CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repoDir, true);
        }
    }

    [Fact]
    public async Task CreateFromBranchAsync_IncludesRoleSuffixInPath()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(new ProcessResult { Success = true, ExitCode = 0 });
        var root = Directory.CreateTempSubdirectory("wsroot").FullName;
        var repoDir = Directory.CreateTempSubdirectory("repo").FullName;
        try
        {
            var manager = BuildManager(fake, root);
            var project = MakeProject(repoDir);

            var result = await manager.CreateFromBranchAsync(project, "rebelgent/task-abc", "reviewer", cancellationToken: CancellationToken.None);

            Assert.Contains("reviewer", result.WorkspacePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(repoDir, true);
        }
    }
}
