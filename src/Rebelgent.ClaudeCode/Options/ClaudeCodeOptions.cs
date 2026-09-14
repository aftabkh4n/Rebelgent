namespace Rebelgent.ClaudeCode.Options;

/// <summary>Configuration for the Claude Code CLI executable.</summary>
public sealed class ClaudeCodeOptions
{
    public const string SectionName = "ClaudeCode";

    /// <summary>
    /// Absolute path to the Claude Code executable.
    /// Set via user-secrets or environment variable — never hardcoded.
    /// Example: C:\Users\Administrator\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;
}
