using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Process;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.Orchestration.Tests;

public class WorkspacePathSafetyTests
{
    private sealed class AlwaysSuccessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new ProcessResult { Success = true, ExitCode = 0 });
    }

    private static GitWorkspaceManager BuildManager(string rootPath)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WorkspaceOptions { RootPath = rootPath });
        return new GitWorkspaceManager(new AlwaysSuccessRunner(), options, NullLogger<GitWorkspaceManager>.Instance);
    }

    [Fact]
    public async Task CreateAsync_WorkspaceUnderRoot_DoesNotThrow()
    {
        var root = Path.GetTempPath();
        var manager = BuildManager(root);

        var project = new ProjectDefinition
        {
            Id = "test",
            Name = "Test",
            RepositoryPath = root,
            DefaultBranch = "main"
        };

        // Will attempt to run git — our fake runner returns success, so this proceeds
        var workspace = await manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None);

        Assert.StartsWith(root, workspace.WorkspacePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyRootPath_ThrowsInvalidOperation()
    {
        var manager = BuildManager(string.Empty);

        var project = new ProjectDefinition
        {
            Id = "test",
            Name = "Test",
            RepositoryPath = @"D:\Projects\Test",
            DefaultBranch = "main"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CreateAsync(project, Guid.NewGuid(), CancellationToken.None));
    }
}
