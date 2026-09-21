using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode.Evolution;

/// <summary>
/// Agent Evolution Manager agent implemented via the Claude Code CLI.
///
/// Hard technical read-only boundary (not just prompt convention):
/// - Runs with <c>--tools ""</c> — disables every built-in tool so the agent cannot write files
///   or run shell commands regardless of prompt/evidence content.
/// - Runs with <c>--permission-mode dontAsk</c> — never a bypass/dangerously-skip-permissions mode.
/// - Runs with <c>--setting-sources project</c> — fresh isolated temp dir prevents user/local
///   Claude filesystem settings from loading while preserving OAuth authentication.
/// - Runs with <c>--strict-mcp-config --mcp-config &lt;empty-config&gt;</c> — prevents any MCP
///   server from loading.
/// - Runs with <c>--disable-slash-commands</c> — prevents slash command execution.
/// - Sets <c>CLAUDE_CODE_DISABLE_AUTO_MEMORY=1</c> in the child environment.
/// - Never receives <c>-w/--worktree</c> — no git/worktree capability.
/// - Runs in a freshly created empty temp directory outside any registered repository.
/// </summary>
public sealed class ClaudeCodeAgentEvolutionManagerAgent : IAgentEvolutionManagerAgent
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<ClaudeCodeOptions> _options;
    private readonly ILogger<ClaudeCodeAgentEvolutionManagerAgent> _logger;

    private const int TimeoutMs = 5 * 60_000;

    internal const string IsolatedDirectoryName = "rebelgent-evolution-analysis";
    internal const string McpConfigFileName = "mcp-config.json";
    private const string EmptyMcpConfig = """{"mcpServers": {}}""";

    public ClaudeCodeAgentEvolutionManagerAgent(
        IProcessRunner processRunner,
        IOptions<ClaudeCodeOptions> options,
        ILogger<ClaudeCodeAgentEvolutionManagerAgent> logger)
    {
        _processRunner = processRunner;
        _options = options;
        _logger = logger;
    }

    public async Task<AgentEvolutionAnalysisOutput> AnalyzeAsync(AgentEvolutionAnalysisInput input, CancellationToken ct = default)
    {
        var executablePath = _options.Value.ExecutablePath;

        if (string.IsNullOrWhiteSpace(executablePath))
            return Fail("ClaudeCode:ExecutablePath is not configured.", EvolutionManagerOutputKind.ProcessFailed);

        string isolatedDirectory;
        string mcpConfigPath;
        try
        {
            isolatedDirectory = CreateIsolatedAnalysisDirectory();
            mcpConfigPath = Path.Combine(isolatedDirectory, McpConfigFileName);
            File.WriteAllText(mcpConfigPath, EmptyMcpConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create isolated analysis environment; refusing to run the Agent Evolution Manager agent.");
            return Fail("Could not create isolated analysis environment. Refusing to run the Agent Evolution Manager agent.", EvolutionManagerOutputKind.ProcessFailed);
        }

        try
        {
            var prompt = AgentEvolutionManagerPromptBuilder.Build(input);

            _logger.LogInformation("Running Agent Evolution Manager agent.");

            var processOptions = new ProcessRunOptions
            {
                FileName = executablePath,
                Arguments =
                [
                    "--print", prompt,
                    "--setting-sources", "project",
                    "--strict-mcp-config",
                    "--mcp-config", mcpConfigPath,
                    "--tools", "",
                    "--permission-mode", "dontAsk",
                    "--disable-slash-commands",
                    "--output-format", "text"
                ],
                WorkingDirectory = isolatedDirectory,
                TimeoutMs = TimeoutMs,
                CloseStdinImmediately = true,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "1"
                }
            };

            var result = await _processRunner.RunAsync(processOptions, ct);

            if (result.TimedOut)
            {
                _logger.LogWarning(
                    "Agent Evolution Manager timed out after {TimeoutMs} ms. Executable={Executable}",
                    TimeoutMs, executablePath);
                return Fail("Agent Evolution Manager agent timed out.", EvolutionManagerOutputKind.ProcessFailed);
            }

            if (!result.Success)
            {
                var stdoutLen = result.StandardOutput?.Length ?? 0;
                var stderrLen = result.StandardError?.Length ?? 0;
                var argSummary = BuildArgumentSummary(processOptions.Arguments, promptIndex: 1);

                _logger.LogWarning(
                    "Agent Evolution Manager failed: ExitCode={ExitCode}, StdoutLength={StdoutLength}, StderrLength={StderrLength}. " +
                    "Executable={Executable}. WorkingDirectory={WorkingDirectory}. Arguments={ArgSummary}",
                    result.ExitCode, stdoutLen, stderrLen, executablePath, isolatedDirectory, argSummary);

                var errorDetail = !string.IsNullOrWhiteSpace(result.StandardError)
                    ? TruncateSafe(result.StandardError, 300)
                    : !string.IsNullOrWhiteSpace(result.StandardOutput)
                        ? $"stdout: {TruncateSafe(result.StandardOutput, 300)}"
                        : "(no output on stdout or stderr)";

                return Fail(
                    $"Agent Evolution Manager exited with code {result.ExitCode}: {errorDetail}",
                    EvolutionManagerOutputKind.ProcessFailed);
            }

            var output = result.StandardOutput ?? string.Empty;
            _logger.LogDebug("Agent Evolution Manager raw output: {Output}", output);

            var parseResult = ParseOutput(output);
            if (parseResult.FailureKind == EvolutionManagerOutputKind.ParseFailed)
            {
                var truncated = TruncateSafe(output, 300);
                _logger.LogWarning("Agent Evolution Manager output could not be parsed. Raw output (first 300 chars): {Output}", truncated);
            }

            return parseResult;
        }
        finally
        {
            TryRemoveDirectory(isolatedDirectory);
        }
    }

    internal static string BuildArgumentSummary(IReadOnlyList<string> arguments, int promptIndex) =>
        string.Join(" ", arguments.Select((arg, i) =>
            i == promptIndex ? $"[prompt:{arg.Length} chars]" :
            arg.Length == 0 ? "\"\"" :
            arg));

    internal static string TruncateSafe(string? text, int maxLength) =>
        string.IsNullOrEmpty(text) ? "(empty)" :
        text.Length <= maxLength ? text.Trim() :
        text[..maxLength].Trim() + $" ... [{text.Length - maxLength} more chars]";

    /// <summary>
    /// Creates a fresh, empty directory under the OS temp root for use as the isolated analysis
    /// working directory. A per-invocation GUID subdirectory guarantees isolation between runs.
    /// </summary>
    internal static string CreateIsolatedAnalysisDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), IsolatedDirectoryName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private void TryRemoveDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove isolated analysis directory {Path}; leaving it in place.", path);
        }
    }

    internal static AgentEvolutionAnalysisOutput ParseOutput(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Fail("Agent Evolution Manager produced no output.", EvolutionManagerOutputKind.EmptyOutput);

        var proposalType = ExtractLineValue(raw, "PROPOSAL_TYPE:");
        var proposedAgentName = ExtractLineValue(raw, "PROPOSED_AGENT_NAME:");
        var proposalTitle = ExtractLineValue(raw, "PROPOSAL_TITLE:");
        var riskLevel = ExtractLineValue(raw, "RISK_LEVEL:");
        var suggestedChange = ExtractLineValue(raw, "SUGGESTED_CHANGE:");
        var description = ExtractDescriptionSection(raw);

        if (string.IsNullOrWhiteSpace(proposalTitle))
            return Fail("Agent Evolution Manager output did not contain a PROPOSAL_TITLE.", EvolutionManagerOutputKind.ParseFailed);

        if (string.IsNullOrWhiteSpace(description))
            description = raw.Trim();

        // N/A for proposed agent name means no specific agent name
        if (string.Equals(proposedAgentName, "N/A", StringComparison.OrdinalIgnoreCase))
            proposedAgentName = null;

        return new AgentEvolutionAnalysisOutput
        {
            Succeeded = true,
            ProposalTitle = proposalTitle.Trim(),
            ProposedAgentName = proposedAgentName?.Trim(),
            ProposalType = proposalType?.Trim(),
            RiskLevel = riskLevel?.Trim(),
            SuggestedChange = suggestedChange?.Trim(),
            Description = description.Trim(),
            RawOutput = raw
        };
    }

    private static string? ExtractLineValue(string text, string key)
    {
        var idx = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + key.Length;
        var end = text.IndexOf('\n', start);
        var value = end >= 0 ? text[start..end] : text[start..];
        return value.Trim();
    }

    private static string ExtractDescriptionSection(string text)
    {
        const string marker = "DESCRIPTION:";
        var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;
        var start = idx + marker.Length;
        return text[start..].Trim();
    }

    private static AgentEvolutionAnalysisOutput Fail(string error, EvolutionManagerOutputKind kind = EvolutionManagerOutputKind.ProcessFailed) => new()
    {
        Succeeded = false,
        ErrorMessage = error,
        FailureKind = kind
    };
}
