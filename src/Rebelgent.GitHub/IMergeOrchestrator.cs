namespace Rebelgent.GitHub;

public interface IMergeOrchestrator
{
    Task<MergeOrchestratorResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default);
}
