using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode.Improvement;

/// <summary>
/// Improvement Analyst agent implemented via the Claude Code CLI.
///
/// Hard technical read-only boundary (not just prompt convention):
/// - Runs with <c>--tools ""</c> — disables every built-in tool (Read, Write, Edit, Bash, Glob,
///   Grep, etc.) so the agent is technically incapable of writing files or running shell commands
///   regardless of prompt/evidence content.
/// - Runs with <c>--permission-mode dontAsk</c> — never a bypass/dangerously-skip-permissions
///   mode. With zero tools this is defense in depth.
/// - Runs with <c>--setting-sources project</c> — because the working directory is a freshly
///   created empty temp directory, this prevents user/local Claude filesystem settings from being
///   loaded while preserving normal OAuth/keychain authentication (intentional: we use the
///   existing Claude Code subscription login, not --bare/ANTHROPIC_API_KEY).
/// - Runs with <c>--strict-mcp-config --mcp-config &lt;empty-config&gt;</c> — an empty
///   <c>{"mcpServers":{}}</c> file written into the isolated directory prevents any MCP server
///   from loading.
/// - Runs with <c>--disable-slash-commands</c> — prevents slash command execution.
/// - Sets <c>CLAUDE_CODE_DISABLE_AUTO_MEMORY=1</c> in the child environment — prevents auto-
///   memory from being loaded.
/// - Never receives <c>-w/--worktree</c> and holds no workspace manager dependency — there is no
///   git/worktree capability reachable from this pipeline at all.
/// - Runs with its working directory set to a freshly created, empty temporary directory outside
///   any registered project repository and outside the Rebelgent repository itself (see
///   <see cref="CreateIsolatedAnalysisDirectory"/>) — never <c>null</c>.
/// - Fails closed: if the executable is not configured or the isolated environment cannot be
///   created, the analyst is never invoked.
/// </summary>
public sealed class ClaudeCodeImprovementAnalystAgent : IImprovementAnalystAgent
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<ClaudeCodeOptions> _options;
    private readonly ILogger<ClaudeCodeImprovementAnalystAgent> _logger;

    private const int TimeoutMs = 5 * 60_000;

    /// <summary>Directory name segment used for every isolated analysis directory — exposed so
    /// tests can assert an invocation's working directory is genuinely isolated.</summary>
    internal const string IsolatedDirectoryName = "rebelgent-improvement-analysis";

    internal const string McpConfigFileName = "mcp-config.json";
    private const string EmptyMcpConfig = """{"mcpServers": {}}""";

    public ClaudeCodeImprovementAnalystAgent(
        IProcessRunner processRunner,
        IOptions<ClaudeCodeOptions> options,
        ILogger<ClaudeCodeImprovementAnalystAgent> logger)
    {
        _processRunner = processRunner;
        _options = options;
        _logger = logger;
    }

    public async Task<ImprovementAnalysisOutput> AnalyzeAsync(ImprovementAnalysisInput input, CancellationToken cancellationToken = default)
    {
        var executablePath = _options.Value.ExecutablePath;

        if (string.IsNullOrWhiteSpace(executablePath))
            return Fail("ClaudeCode:ExecutablePath is not configured.", AnalystOutputKind.ProcessFailed);

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
            // Fail closed — never fall back to running the analyst without a verified isolated
            // environment (e.g. by leaving WorkingDirectory null, which would silently inherit
            // the host process's own working directory inside the Rebelgent repo).
            _logger.LogError(ex, "Failed to create isolated analysis environment; refusing to run the Improvement Analyst agent.");
            return Fail("Could not create isolated analysis environment. Refusing to run the Improvement Analyst agent.", AnalystOutputKind.ProcessFailed);
        }

        try
        {
            var prompt = ImprovementAnalystPromptBuilder.Build(input);

            _logger.LogInformation("Running Improvement Analyst agent for pattern: {Title}", input.Title);

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

            var result = await _processRunner.RunAsync(processOptions, cancellationToken);

            if (result.TimedOut)
            {
                _logger.LogWarning(
                    "Improvement Analyst agent timed out for pattern '{Title}' after {TimeoutMs} ms. " +
                    "Executable={Executable}, WorkingDirectory={WorkingDirectory}",
                    input.Title, TimeoutMs, executablePath, isolatedDirectory);
                return Fail("Improvement Analyst agent timed out.", AnalystOutputKind.ProcessFailed);
            }

            if (!result.Success)
            {
                var stdoutLen = result.StandardOutput?.Length ?? 0;
                var stderrLen = result.StandardError?.Length ?? 0;
                var stdoutSample = TruncateSafe(result.StandardOutput, 300);
                var stderrSample = TruncateSafe(result.StandardError, 300);
                var argSummary = BuildArgumentSummary(processOptions.Arguments, promptIndex: 1);

                _logger.LogWarning(
                    "Improvement Analyst agent failed for pattern '{Title}': " +
                    "ExitCode={ExitCode}, StdoutLength={StdoutLength}, StderrLength={StderrLength}. " +
                    "Executable={Executable}. WorkingDirectory={WorkingDirectory}. " +
                    "Arguments={ArgSummary}. Stdout={StdoutSample}. Stderr={StderrSample}",
                    input.Title, result.ExitCode, stdoutLen, stderrLen,
                    executablePath, isolatedDirectory, argSummary,
                    stdoutSample, stderrSample);

                // When stderr is empty (e.g. Claude Code prints its error to stdout),
                // surface stdout instead so the failure reason is not silently blank.
                var errorDetail = !string.IsNullOrWhiteSpace(result.StandardError)
                    ? TruncateSafe(result.StandardError, 300)
                    : !string.IsNullOrWhiteSpace(result.StandardOutput)
                        ? $"stdout: {TruncateSafe(result.StandardOutput, 300)}"
                        : "(no output on stdout or stderr)";

                return Fail(
                    $"Improvement Analyst agent exited with code {result.ExitCode}: {errorDetail}",
                    AnalystOutputKind.ProcessFailed);
            }

            var output = result.StandardOutput ?? string.Empty;
            _logger.LogDebug("Improvement Analyst raw output: {Output}", output);

            var parseResult = ParseOutput(output);
            if (parseResult.FailureKind == AnalystOutputKind.ParseFailed)
            {
                var truncated = output.Length > 300 ? output[..300] : output;
                _logger.LogWarning("Improvement Analyst output could not be parsed for pattern '{Title}'. Raw output (first 300 chars): {Output}", input.Title, truncated);
            }

            return parseResult;
        }
        finally
        {
            TryRemoveDirectory(isolatedDirectory);
        }
    }

    // Returns a redacted summary of the argument list for diagnostic logging.
    // The prompt at promptIndex is replaced with its length to avoid logging evidence content.
    internal static string BuildArgumentSummary(IReadOnlyList<string> arguments, int promptIndex) =>
        string.Join(" ", arguments.Select((arg, i) =>
            i == promptIndex ? $"[prompt:{arg.Length} chars]" :
            arg.Length == 0 ? "\"\"" :
            arg));

    // Returns a safe truncated string for log output. Never returns null.
    internal static string TruncateSafe(string? text, int maxLength) =>
        string.IsNullOrEmpty(text) ? "(empty)" :
        text.Length <= maxLength ? text.Trim() :
        text[..maxLength].Trim() + $" ... [{text.Length - maxLength} more chars]";

    /// <summary>
    /// Creates a fresh, empty directory under the OS temp root, outside the Rebelgent repository
    /// and outside any registered project repository, to use as the Improvement Analyst's working
    /// directory. A per-invocation GUID subdirectory guarantees isolation between runs.
    /// </summary>
    internal static string CreateIsolatedAnalysisDirectory()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), IsolatedDirectoryName, Guid.NewGuid().ToString("N"));
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

    internal static ImprovementAnalysisOutput ParseOutput(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Fail("Improvement Analyst produced no output.", AnalystOutputKind.EmptyOutput);

        var title = ExtractLineValue(raw, "PROPOSAL_TITLE:");
        var targetArea = ExtractLineValue(raw, "TARGET_AREA:");
        var riskLevel = ExtractLineValue(raw, "RISK_LEVEL:");
        var suggestedChange = ExtractLineValue(raw, "SUGGESTED_CHANGE:");
        var description = ExtractDescriptionSection(raw);

        if (string.IsNullOrWhiteSpace(title))
            return Fail("Improvement Analyst output did not contain a PROPOSAL_TITLE.", AnalystOutputKind.ParseFailed);

        if (string.IsNullOrWhiteSpace(description))
            description = raw.Trim();

        return new ImprovementAnalysisOutput
        {
            Succeeded = true,
            ProposalTitle = title.Trim(),
            TargetArea = targetArea?.Trim(),
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

    private static ImprovementAnalysisOutput Fail(string error, AnalystOutputKind kind = AnalystOutputKind.ProcessFailed) => new()
    {
        Succeeded = false,
        ErrorMessage = error,
        FailureKind = kind
    };
}
