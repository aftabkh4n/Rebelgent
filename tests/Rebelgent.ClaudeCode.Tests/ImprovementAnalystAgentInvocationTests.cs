using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.ClaudeCode.Improvement;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode.Tests;

/// <summary>
/// Proves the Improvement Analyst's read-only boundary is enforced at the process-invocation
/// level (the exact <see cref="ProcessRunOptions"/> passed to the Claude Code CLI), not merely by
/// prompt text. These are the technical mechanisms a prompt injection cannot override.
/// </summary>
public class ImprovementAnalystAgentInvocationTests
{
    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessRunOptions> Calls { get; } = [];
        public ProcessResult NextResult { get; set; } = new()
        {
            Success = true,
            ExitCode = 0,
            StandardOutput = "PROPOSAL_TITLE: Title\nTARGET_AREA: Area\nRISK_LEVEL: Low\nSUGGESTED_CHANGE: change\nDESCRIPTION:\ndesc"
        };

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken = default)
        {
            Calls.Add(options);
            return Task.FromResult(NextResult);
        }
    }

    private static ClaudeCodeImprovementAnalystAgent BuildAgent(FakeProcessRunner fake, string executablePath = @"C:\claude\claude.exe")
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ClaudeCodeOptions { ExecutablePath = executablePath });
        return new ClaudeCodeImprovementAnalystAgent(fake, options, NullLogger<ClaudeCodeImprovementAnalystAgent>.Instance);
    }

    private static ImprovementAnalysisInput MakeInput() => new()
    {
        Title = "Developer repeatedly fails the build",
        Category = "BuildFailure",
        Source = "BackendDeveloper",
        Occurrences = 3,
        Evidence = "evidence"
    };

    [Fact]
    public async Task AnalyzeAsync_DisablesAllTools()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments.ToList();
        var toolsIndex = args.IndexOf("--tools");
        Assert.True(toolsIndex >= 0, "Expected --tools to be passed.");
        Assert.Equal(string.Empty, args[toolsIndex + 1]);
    }

    [Fact]
    public async Task AnalyzeAsync_NeverUsesBypassPermissionsOrSkipPermissions()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments;
        Assert.DoesNotContain(args, a => a.Contains("bypass", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, a => a.Contains("dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, a => a.Contains("allow-dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeAsync_UsesSafeDefaultPermissionMode()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments.ToList();
        var modeIndex = args.IndexOf("--permission-mode");
        Assert.True(modeIndex >= 0);
        Assert.Equal("default", args[modeIndex + 1]);
    }

    [Fact]
    public async Task AnalyzeAsync_NeverRequestsAWorktree()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments;
        Assert.DoesNotContain("-w", args);
        Assert.DoesNotContain("--worktree", args);
    }

    [Fact]
    public async Task AnalyzeAsync_WorkingDirectoryIsNeverNull()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        Assert.NotNull(fake.Calls.Single().WorkingDirectory);
    }

    [Fact]
    public async Task AnalyzeAsync_WorkingDirectoryIsIsolatedOutsideAnyRepository()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var workingDirectory = fake.Calls.Single().WorkingDirectory!;

        // Must be under the OS temp root in the dedicated isolated-analysis subtree, never
        // inside the Rebelgent repository or a registered project's RepositoryPath.
        Assert.StartsWith(Path.GetTempPath(), workingDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ClaudeCodeImprovementAnalystAgent.IsolatedDirectoryName, workingDirectory);
        Assert.DoesNotContain(@"D:\Projects\Rebelgent", workingDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_EachInvocationGetsAFreshIsolatedDirectory()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());
        await agent.AnalyzeAsync(MakeInput());

        Assert.NotEqual(fake.Calls[0].WorkingDirectory, fake.Calls[1].WorkingDirectory);
    }

    [Fact]
    public async Task AnalyzeAsync_CleansUpIsolatedDirectoryAfterCompletion()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var workingDirectory = fake.Calls.Single().WorkingDirectory!;
        Assert.False(Directory.Exists(workingDirectory));
    }

    [Fact]
    public async Task AnalyzeAsync_ExecutablePathNotConfigured_FailsClosedWithoutInvokingProcess()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake, executablePath: string.Empty);

        var result = await agent.AnalyzeAsync(MakeInput());

        Assert.False(result.Succeeded);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void CreateIsolatedAnalysisDirectory_CreatesARealDirectoryOutsideAnyRepo()
    {
        var path = ClaudeCodeImprovementAnalystAgent.CreateIsolatedAnalysisDirectory();
        try
        {
            Assert.True(Directory.Exists(path));
            Assert.Empty(Directory.GetFileSystemEntries(path));
            Assert.StartsWith(Path.GetTempPath(), path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
