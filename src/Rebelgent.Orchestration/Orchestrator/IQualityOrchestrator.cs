namespace Rebelgent.Orchestration.Orchestrator;

/// <summary>
/// Drives the QA and code review pipeline for a completed developer task.
/// Developer must never QA or review its own work.
/// </summary>
public interface IQualityOrchestrator
{
    Task<QualityOrchestrationResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default);
}
