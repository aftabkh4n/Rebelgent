using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode.Improvement;

/// <summary>
/// Improvement Analyst agent implemented via the Claude Code CLI.
///
/// Hard technical read-only boundary (not just prompt convention):
/// - Runs with <c>--tools ""</c>, which disables every built-in tool — the agent has no Read,
///   Write, Edit, Bash, Glob, Grep, or any other tool available at all, so it is technically
///   incapable of writing files, running shell commands, or mutating git/worktrees regardless
///   of what the prompt or evidence content says.
/// - Runs with <c>--permission-mode default</c> — never a bypass/dangerously-skip-permissions
///   mode. With zero tools available this is defense in depth, not the primary control.
/// - Never receives <c>-w/--worktree</c> and holds no <see cref="Rebelgent.Orchestration.Workspace.IWorkspaceManager"/>
///   dependency anywhere in this class or in <see cref="ImprovementOrchestrator"/> — there is no
///   git/worktree capability reachable from this pipeline at all.
/// - Runs with its working directory set to a freshly created, empty temporary directory outside
///   any registered project repository and outside the Rebelgent repository itself (see
///   <see cref="CreateIsolatedAnalysisDirectory"/>) — never <c>null</c>, which would silently
///   inherit the host process's own working directory (inside this repo).
/// - Fails closed: if the executable is not configured or the isolated directory cannot be
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
        try
        {
            isolatedDirectory = CreateIsolatedAnalysisDirectory();
        }
        catch (Exception ex)
        {
            // Fail closed — never fall back to running the analyst without a verified isolated
            // working directory (e.g. by leaving WorkingDirectory null, which would silently
            // inherit this host process's own working directory inside the Rebelgent repo).
            _logger.LogError(ex, "Failed to create an isolated analysis directory; refusing to run the Improvement Analyst agent.");
            return Fail("Could not create an isolated read-only analysis directory. Refusing to run the Improvement Analyst agent.", AnalystOutputKind.ProcessFailed);
        }

        try
        {
            var prompt = ImprovementAnalystPromptBuilder.Build(input);

            _logger.LogInformation("Running Improvement Analyst agent for pattern: {Title}", input.Title);

            var result = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = executablePath,
                Arguments =
                [
                    "-p", prompt,
                    "--permission-mode", "default",
                    "--tools", "",
                    "--output-format", "text"
                ],
                WorkingDirectory = isolatedDirectory,
                TimeoutMs = TimeoutMs
            }, cancellationToken);

            if (result.TimedOut)
            {
                _logger.LogWarning("Improvement Analyst agent timed out for pattern: {Title}", input.Title);
                return Fail("Improvement Analyst agent timed out.", AnalystOutputKind.ProcessFailed);
            }

            if (!result.Success)
            {
                _logger.LogWarning("Improvement Analyst agent failed (exit {ExitCode}): {Error}", result.ExitCode, result.StandardError);
                return Fail($"Improvement Analyst agent exited with code {result.ExitCode}: {result.StandardError}", AnalystOutputKind.ProcessFailed);
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
