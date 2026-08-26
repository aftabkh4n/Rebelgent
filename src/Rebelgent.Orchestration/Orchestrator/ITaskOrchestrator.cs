namespace Rebelgent.Orchestration.Orchestrator;

/// <summary>Drives the full agent execution pipeline for one task.</summary>
public interface ITaskOrchestrator
{
    Task<OrchestrationResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default);
}
