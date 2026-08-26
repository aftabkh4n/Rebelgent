namespace Rebelgent.Orchestration.Agents;

/// <summary>Result of a pre-execution availability check on a coding agent runner.</summary>
public sealed class AgentValidationResult
{
    public bool IsReady { get; init; }
    public string? ErrorMessage { get; init; }
}
