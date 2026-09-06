namespace Rebelgent.Orchestration.Orchestrator;

public sealed class QualityOrchestrationResult
{
    public bool Succeeded { get; init; }
    public QaOutcome QaOutcome { get; init; }
    public ReviewDecision ReviewDecision { get; init; }
    public string? QaFindings { get; init; }
    public string? ReviewFindings { get; init; }
    public string? ErrorMessage { get; init; }
    public string Summary { get; init; } = string.Empty;
}
