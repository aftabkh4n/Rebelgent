namespace Rebelgent.GitHub.Release;

public interface IReleaseOrchestrator
{
    Task<ReleaseOrchestratorResult> PrepareAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<ReleaseOrchestratorResult> ApproveAndPublishAsync(Guid taskId, CancellationToken cancellationToken = default);
}
