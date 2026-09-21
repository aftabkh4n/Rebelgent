using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.ClaudeCode.Evolution;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode.Tests;

/// <summary>
/// Proves the Agent Evolution Manager's read-only boundary is enforced at the process-invocation
/// level (the exact <see cref="ProcessRunOptions"/> passed to the Claude Code CLI), not merely by
/// prompt text. These are the technical mechanisms a prompt injection cannot override.
/// </summary>
public class AgentEvolutionManagerAgentInvocationTests
{
    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessRunOptions> Calls { get; } = [];
        public string? CapturedMcpConfigContent { get; private set; }
        public ProcessResult NextResult { get; set; } = new()
        {
            Success = true,
            ExitCode = 0,
            StandardOutput = "PROPOSAL_TITLE: Introduce a QA specialist agent\nPROPOSAL_TYPE: CreateAgent\nPROPOSED_AGENT_NAME: QaSpecialistAgent\nRISK_LEVEL: Low\nSUGGESTED_CHANGE: Create a new QA agent\nDESCRIPTION:\nA dedicated QA agent to improve test coverage."
        };

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(options);

            var args = options.Arguments.ToList();
            var mcpIdx = args.IndexOf("--mcp-config");
            if (mcpIdx >= 0 && mcpIdx + 1 < args.Count)
            {
                var mcpPath = args[mcpIdx + 1];
                if (File.Exists(mcpPath))
                    CapturedMcpConfigContent = File.ReadAllText(mcpPath);
            }

            return Task.FromResult(NextResult);
        }
    }

    private static ClaudeCodeAgentEvolutionManagerAgent BuildAgent(FakeProcessRunner fake, string executablePath = @"C:\claude\claude.exe")
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ClaudeCodeOptions { ExecutablePath = executablePath });
        return new ClaudeCodeAgentEvolutionManagerAgent(fake, options, NullLogger<ClaudeCodeAgentEvolutionManagerAgent>.Instance);
    }

    private static AgentEvolutionAnalysisInput MakeInput() => new()
    {
        PatternSummary = "Build failures are increasing",
        AgentPerformanceSummary = "Developer pass rate: 60%",
        FailureCategorySummary = "BuildFailure: 5, TestFailure: 2",
        ExistingAgentsSummary = "BackendDeveloper: Active",
        Evidence = "Evidence of pattern"
    };

    // ── Prompt delivery ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_UsesExplicitPrintFlag_NotShortForm()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments;
        Assert.Contains("--print", args);
        Assert.DoesNotContain("-p", args);
    }

    [Fact]
    public async Task AnalyzeAsync_PromptIsPresentInArgumentList()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);
        var input = MakeInput();

        await agent.AnalyzeAsync(input);

        var args = fake.Calls.Single().Arguments.ToList();
        var printIndex = args.IndexOf("--print");
        Assert.True(printIndex >= 0, "--print must be present.");
        Assert.True(printIndex + 1 < args.Count, "--print must be followed by the prompt argument.");

        var promptArg = args[printIndex + 1];
        Assert.False(string.IsNullOrWhiteSpace(promptArg), "Prompt argument must not be empty or whitespace.");
        Assert.Contains(input.PatternSummary, promptArg);
        Assert.Contains(input.Evidence, promptArg);
    }

    [Fact]
    public async Task AnalyzeAsync_PromptWithSpecialCharacters_PassedAsSingleArgument()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);
        var input = new AgentEvolutionAnalysisInput
        {
            PatternSummary = "Build failures",
            AgentPerformanceSummary = "60% pass rate",
            FailureCategorySummary = "BuildFailure: 5",
            ExistingAgentsSummary = "BackendDeveloper: Active",
            Evidence = "Line 1: build failed\nLine 2: error 'CS0001'\n--- unexpected --- delimiter\nspecial chars: <>&|\"'"
        };

        await agent.AnalyzeAsync(input);

        var args = fake.Calls.Single().Arguments.ToList();
        var printIndex = args.IndexOf("--print");
        Assert.True(printIndex >= 0);
        var promptArg = args[printIndex + 1];

        Assert.Contains("build failed", promptArg);
        Assert.Contains("CS0001", promptArg);
        Assert.Contains("special chars", promptArg);
        Assert.Equal("--setting-sources", args[printIndex + 2]);
    }

    [Fact]
    public async Task AnalyzeAsync_EvidenceDelimitersRemainIntact_InPromptArgument()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments.ToList();
        var printIndex = args.IndexOf("--print");
        var promptArg = args[printIndex + 1];

        Assert.Contains("BEGIN EVIDENCE", promptArg);
        Assert.Contains("END EVIDENCE", promptArg);
    }

    [Fact]
    public async Task AnalyzeAsync_StdinIsClosedImmediately()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        Assert.True(fake.Calls.Single().CloseStdinImmediately,
            "CloseStdinImmediately must be set so Claude does not wait for inherited stdin.");
    }

    // ── Tool / permission boundary ────────────────────────────────────────────

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
    }

    [Fact]
    public async Task AnalyzeAsync_UsesPermissionModeDontAsk()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments.ToList();
        var modeIndex = args.IndexOf("--permission-mode");
        Assert.True(modeIndex >= 0);
        Assert.Equal("dontAsk", args[modeIndex + 1]);
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

    // ── Isolation / authentication boundary ──────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_UsesSettingSourcesProject()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments.ToList();
        var idx = args.IndexOf("--setting-sources");
        Assert.True(idx >= 0, "--setting-sources must be passed.");
        Assert.Equal("project", args[idx + 1]);
    }

    [Fact]
    public async Task AnalyzeAsync_UsesStrictMcpConfig()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments;
        Assert.Contains("--strict-mcp-config", args);
        Assert.Contains("--mcp-config", args);
    }

    [Fact]
    public async Task AnalyzeAsync_McpConfigFileContainsZeroServers()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        Assert.NotNull(fake.CapturedMcpConfigContent);
        using var doc = JsonDocument.Parse(fake.CapturedMcpConfigContent);
        var mcpServers = doc.RootElement.GetProperty("mcpServers");
        Assert.Equal(JsonValueKind.Object, mcpServers.ValueKind);
        Assert.Empty(mcpServers.EnumerateObject());
    }

    [Fact]
    public async Task AnalyzeAsync_DisableSlashCommandsPresent()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        Assert.Contains("--disable-slash-commands", fake.Calls.Single().Arguments);
    }

    [Fact]
    public async Task AnalyzeAsync_SetsDisableAutoMemoryEnvironmentVariable()
    {
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var envVars = fake.Calls.Single().EnvironmentVariables;
        Assert.True(envVars.TryGetValue("CLAUDE_CODE_DISABLE_AUTO_MEMORY", out var value),
            "CLAUDE_CODE_DISABLE_AUTO_MEMORY must be set in child process environment.");
        Assert.Equal("1", value);
    }

    // ── Working directory isolation ───────────────────────────────────────────

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

        Assert.StartsWith(Path.GetTempPath(), workingDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ClaudeCodeAgentEvolutionManagerAgent.IsolatedDirectoryName, workingDirectory);
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
}
