using Rebelgent.GitHub.Release;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeReleaseOrchestrator : IReleaseOrchestrator
{
    public ReleaseOrchestratorResult PrepareResult { get; set; } = new()
    {
        Succeeded = true,
        Version = "1.0.0",
        TagName = "v1.0.0",
        Title = "Release 1.0.0",
        Status = Rebelgent.Core.Domain.ReleaseStatus.Prepared,
        Summary = "Release prepared: v1.0.0 — Release 1.0.0"
    };

    public ReleaseOrchestratorResult ApproveResult { get; set; } = new()
    {
        Succeeded = true,
        Version = "1.0.0",
        TagName = "v1.0.0",
        Title = "Release 1.0.0",
        GitHubReleaseUrl = "https://github.com/org/repo/releases/tag/v1.0.0",
        Status = Rebelgent.Core.Domain.ReleaseStatus.Published,
        Summary = "Release v1.0.0 published.\nhttps://github.com/org/repo/releases/tag/v1.0.0"
    };

    public Guid? LastPrepareTaskId { get; private set; }
    public Guid? LastApproveTaskId { get; private set; }

    public Task<ReleaseOrchestratorResult> PrepareAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        LastPrepareTaskId = taskId;
        return Task.FromResult(PrepareResult);
    }

    public Task<ReleaseOrchestratorResult> ApproveAndPublishAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        LastApproveTaskId = taskId;
        return Task.FromResult(ApproveResult);
    }
}
