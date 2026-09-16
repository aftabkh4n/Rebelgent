using System.Text.Json;
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
        public string? CapturedMcpConfigContent { get; private set; }
        public ProcessResult NextResult { get; set; } = new()
        {
            Success = true,
            ExitCode = 0,
            StandardOutput = "PROPOSAL_TITLE: Title\nTARGET_AREA: Area\nRISK_LEVEL: Low\nSUGGESTED_CHANGE: change\nDESCRIPTION:\ndesc"
        };

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(options);

            // Read the MCP config while the isolated directory still exists (before cleanup).
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

    // ── Prompt delivery ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_UsesExplicitPrintFlag_NotShortForm()
    {
        // Must use --print (long form), never -p, so there is no ambiguity with
        // other short flags such as --project that share the same character.
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
        Assert.Contains(input.Title, promptArg);
        Assert.Contains(input.Evidence, promptArg);
    }

    [Fact]
    public async Task AnalyzeAsync_PromptWithSpecialCharacters_PassedAsSingleArgument()
    {
        // The prompt contains spaces, newlines, brackets, hyphens, and quotes — it must
        // arrive as ONE entry in ArgumentList, not split into multiple arguments.
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);
        var input = new ImprovementAnalysisInput
        {
            Title = "Developer repeatedly fails the build",
            Category = "BuildFailure",
            Source = "BackendDeveloper",
            Occurrences = 3,
            Evidence = "Line 1: build failed\nLine 2: error 'CS0001'\n--- unexpected --- delimiter\nspecial chars: <>&|\"'"
        };

        await agent.AnalyzeAsync(input);

        var args = fake.Calls.Single().Arguments.ToList();
        var printIndex = args.IndexOf("--print");
        Assert.True(printIndex >= 0);
        var promptArg = args[printIndex + 1];

        // The entire prompt is ONE argument — every other arg is a known flag or its value.
        Assert.Contains("build failed", promptArg);
        Assert.Contains("CS0001", promptArg);
        Assert.Contains("special chars", promptArg);
        // Subsequent argument must be a flag, not more prompt text.
        Assert.Equal("--setting-sources", args[printIndex + 2]);
    }

    [Fact]
    public async Task AnalyzeAsync_EvidenceDelimitersRemainIntact_InPromptArgument()
    {
        // The BEGIN/END EVIDENCE delimiters that separate system instructions from
        // untrusted evidence must arrive verbatim inside the single prompt argument.
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
        // CloseStdinImmediately = true ensures the process receives EOF on stdin at once
        // rather than waiting for inherited stdin from the host process.
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
        Assert.DoesNotContain(args, a => a.Contains("allow-dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase));
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
    public async Task AnalyzeAsync_NoBareFlag()
    {
        // --bare skips OAuth/keychain reads and requires ANTHROPIC_API_KEY.
        // We use the existing Claude Code subscription login, so --bare must never appear.
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);

        await agent.AnalyzeAsync(MakeInput());

        var args = fake.Calls.Single().Arguments;
        Assert.DoesNotContain("--bare", args);
    }

    [Fact]
    public async Task AnalyzeAsync_UsesSettingSourcesProject()
    {
        // --setting-sources project prevents user/local Claude filesystem settings from being
        // loaded while preserving normal OAuth authentication.
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
        // The MCP config passed to --mcp-config must be an empty {"mcpServers":{}} object
        // so no MCP server can be loaded regardless of user/project configuration.
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
        // CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 prevents the analyst from loading auto-memory.
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

    // ── Failure diagnostics ───────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ExitCode1_EmptyStderr_PopulatedStdout_IncludesStdoutInErrorMessage()
    {
        // Claude Code sometimes prints its error to stdout rather than stderr.
        // The error message returned to callers must surface stdout in this case.
        var fake = new FakeProcessRunner
        {
            NextResult = new ProcessResult
            {
                Success = false,
                ExitCode = 1,
                StandardOutput = "Error: unrecognized option '--tools'\nUsage: claude [options]",
                StandardError = string.Empty
            }
        };
        var agent = BuildAgent(fake);

        var result = await agent.AnalyzeAsync(MakeInput());

        Assert.False(result.Succeeded);
        Assert.Equal(AnalystOutputKind.ProcessFailed, result.FailureKind);
        Assert.NotNull(result.ErrorMessage);
        // The stdout content must appear in the error message — not just the exit code.
        Assert.Contains("unrecognized option", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_ExitCode1_BothOutputsEmpty_ErrorMessageIndicatesNoOutput()
    {
        var fake = new FakeProcessRunner
        {
            NextResult = new ProcessResult
            {
                Success = false,
                ExitCode = 1,
                StandardOutput = string.Empty,
                StandardError = string.Empty
            }
        };
        var agent = BuildAgent(fake);

        var result = await agent.AnalyzeAsync(MakeInput());

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
        // Must contain the exit code.
        Assert.Contains("1", result.ErrorMessage);
        // Must indicate no output — not silently omit the fact.
        Assert.Contains("no output", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_TimedOut_ErrorMessageIndicatesTimeout()
    {
        var fake = new FakeProcessRunner
        {
            NextResult = new ProcessResult
            {
                Success = false,
                ExitCode = -1,
                TimedOut = true,
                StandardOutput = string.Empty,
                StandardError = string.Empty
            }
        };
        var agent = BuildAgent(fake);

        var result = await agent.AnalyzeAsync(MakeInput());

        Assert.False(result.Succeeded);
        Assert.Equal(AnalystOutputKind.ProcessFailed, result.FailureKind);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_Cancelled_PropagatesOperationCanceledException()
    {
        // When the CancellationToken is signalled before or during the process run,
        // the exception must propagate — the agent must not swallow it.
        var fake = new FakeProcessRunner();
        var agent = BuildAgent(fake);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => agent.AnalyzeAsync(MakeInput(), cts.Token));
    }

    // ── BuildArgumentSummary / TruncateSafe unit tests ────────────────────────

    [Fact]
    public void BuildArgumentSummary_RedactsPromptAtSpecifiedIndex()
    {
        var args = new[] { "--print", "this is the very long prompt text", "--permission-mode", "default" };

        var summary = ClaudeCodeImprovementAnalystAgent.BuildArgumentSummary(args, promptIndex: 1);

        Assert.DoesNotContain("very long prompt text", summary);
        Assert.Contains("--print", summary);
        Assert.Contains("[prompt:", summary);
        Assert.Contains("--permission-mode", summary);
        Assert.Contains("default", summary);
    }

    [Fact]
    public void BuildArgumentSummary_RepresentsEmptyStringArgExplicitly()
    {
        var args = new[] { "--tools", "" };

        var summary = ClaudeCodeImprovementAnalystAgent.BuildArgumentSummary(args, promptIndex: 99);

        Assert.Contains("--tools", summary);
        Assert.Contains("\"\"", summary);
    }

    [Fact]
    public void TruncateSafe_NullOrEmpty_ReturnsEmptyMarker()
    {
        Assert.Equal("(empty)", ClaudeCodeImprovementAnalystAgent.TruncateSafe(null, 100));
        Assert.Equal("(empty)", ClaudeCodeImprovementAnalystAgent.TruncateSafe(string.Empty, 100));
    }

    [Fact]
    public void TruncateSafe_ShortText_ReturnsUnchanged()
    {
        Assert.Equal("hello", ClaudeCodeImprovementAnalystAgent.TruncateSafe("hello", 100));
    }

    [Fact]
    public void TruncateSafe_LongText_TruncatesAndIndicatesRemainder()
    {
        var text = new string('x', 500);

        var result = ClaudeCodeImprovementAnalystAgent.TruncateSafe(text, 100);

        Assert.StartsWith(new string('x', 100), result);
        Assert.Contains("more chars", result);
    }
}
