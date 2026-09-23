namespace Rebelgent.Orchestration.Orchestrator;

using Rebelgent.Core.Authority;

/// <summary>Drives the full agent execution pipeline for one task.</summary>
public interface ITaskOrchestrator
{
    Task<OrchestrationResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<OrchestrationResult> RetryAsync(Guid taskId, HumanPrincipal human, CancellationToken cancellationToken = default);
}
