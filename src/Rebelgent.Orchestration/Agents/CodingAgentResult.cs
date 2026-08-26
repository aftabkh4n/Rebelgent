namespace Rebelgent.Orchestration.Agents;

/// <summary>Output from a coding agent run.</summary>
public sealed class CodingAgentResult
{
    public bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public bool TimedOut { get; init; }
}
