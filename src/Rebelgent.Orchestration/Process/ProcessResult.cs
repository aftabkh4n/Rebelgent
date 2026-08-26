namespace Rebelgent.Orchestration.Process;

/// <summary>Output of a completed process run.</summary>
public sealed class ProcessResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
}
