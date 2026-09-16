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

    /// <summary>
    /// Zero-based indices of <see cref="Arguments"/> entries that contain secrets (e.g. API keys).
    /// The actual argument values are passed to the process unchanged.
    /// Only the debug-log entry is replaced with "***" at these positions.
    /// </summary>
    public IReadOnlyCollection<int> SecretArgumentIndices { get; init; } = [];

    /// <summary>
    /// When <see langword="true"/>, stdin is redirected and immediately closed after the process
    /// starts so the child receives EOF at once. Use when the prompt is supplied entirely via
    /// CLI arguments (e.g. <c>--print "&lt;prompt&gt;"</c>) and the process must not wait for
    /// inherited stdin from the host process.
    /// </summary>
    public bool CloseStdinImmediately { get; init; }

    /// <summary>
    /// Additional environment variables to set for the child process. Merged into the inherited
    /// environment — existing variables not listed here are preserved unchanged.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>(0);
}
