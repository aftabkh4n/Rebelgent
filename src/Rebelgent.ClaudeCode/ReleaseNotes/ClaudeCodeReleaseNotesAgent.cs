using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode.ReleaseNotes;

/// <summary>
/// Release Manager agent implemented via the Claude Code CLI.
/// Runs in non-interactive print mode without a git worktree.
/// Never modifies source, never pushes, never tags or deploys.
/// </summary>
public sealed class ClaudeCodeReleaseNotesAgent : IReleaseNotesAgent
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<ClaudeCodeOptions> _options;
    private readonly ILogger<ClaudeCodeReleaseNotesAgent> _logger;

    private const int TimeoutMs = 5 * 60_000;

    public ClaudeCodeReleaseNotesAgent(
        IProcessRunner processRunner,
        IOptions<ClaudeCodeOptions> options,
        ILogger<ClaudeCodeReleaseNotesAgent> logger)
    {
        _processRunner = processRunner;
        _options = options;
        _logger = logger;
    }

    public async Task<ReleaseNotesOutput> PrepareAsync(ReleaseNotesInput input, CancellationToken cancellationToken = default)
    {
        var executablePath = _options.Value.ExecutablePath;

        if (string.IsNullOrWhiteSpace(executablePath))
            return Fail("ClaudeCode:ExecutablePath is not configured.");

        var prompt = ReleaseManagerPromptBuilder.Build(input);

        _logger.LogInformation("Running Release Manager agent for task: {Title}", input.TaskTitle);

        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = executablePath,
            Arguments = ["-p", prompt, "--permission-mode", "bypassPermissions", "--output-format", "text"],
            WorkingDirectory = null,
            TimeoutMs = TimeoutMs
        }, cancellationToken);

        if (result.TimedOut)
        {
            _logger.LogWarning("Release Manager agent timed out for task: {Title}", input.TaskTitle);
            return Fail("Release Manager agent timed out.");
        }

        if (!result.Success)
        {
            _logger.LogWarning("Release Manager agent failed (exit {ExitCode}): {Error}", result.ExitCode, result.StandardError);
            return Fail($"Release Manager agent exited with code {result.ExitCode}: {result.StandardError}");
        }

        var output = result.StandardOutput ?? string.Empty;
        _logger.LogDebug("Release Manager raw output: {Output}", output);

        return ParseOutput(output);
    }

    internal static ReleaseNotesOutput ParseOutput(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Fail("Release Manager produced no output.");

        var version = ExtractLineValue(raw, "RELEASE_VERSION:");
        var title = ExtractLineValue(raw, "RELEASE_TITLE:");
        var breakingStr = ExtractLineValue(raw, "BREAKING_CHANGES:");
        var notes = ExtractNotesSection(raw);

        if (string.IsNullOrWhiteSpace(version))
        {
            var extracted = SemverValidator.TryExtract(raw);
            if (extracted is not null)
                version = extracted;
            else
                return Fail("Release Manager output did not contain a valid RELEASE_VERSION.");
        }

        if (!SemverValidator.IsValid(version))
            return Fail($"Release Manager suggested invalid version '{version}'. Expected MAJOR.MINOR.PATCH format.");

        if (string.IsNullOrWhiteSpace(title))
            title = $"Release {version}";

        if (string.IsNullOrWhiteSpace(notes))
            notes = raw.Trim();

        var hasBreakingChanges = string.Equals(breakingStr?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        return new ReleaseNotesOutput
        {
            Succeeded = true,
            Version = version.Trim(),
            Title = title.Trim(),
            Notes = notes.Trim(),
            HasBreakingChanges = hasBreakingChanges,
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

    private static string ExtractNotesSection(string text)
    {
        const string marker = "RELEASE_NOTES:";
        var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;
        var start = idx + marker.Length;
        return text[start..].Trim();
    }

    private static ReleaseNotesOutput Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error
    };
}
