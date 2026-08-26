namespace Rebelgent.Orchestration.Orchestrator;

/// <summary>Summary result returned to the caller after an orchestration run completes.</summary>
public sealed class OrchestrationResult
{
    public bool Succeeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}
