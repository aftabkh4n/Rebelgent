namespace Rebelgent.Orchestration.Process;

/// <summary>Parameters for a safe process execution.</summary>
public sealed class ProcessRunOptions
{
    /// <summary>Executable name or absolute path. Never a shell interpreter.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Arguments passed as a structured list — never concatenated into a shell string.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    /// <summary>Milliseconds before the process is killed. Default: 60 000 ms.</summary>
    public int TimeoutMs { get; init; } = 60_000;
}
