using Rebelgent.GitHub;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeMergeOrchestrator : IMergeOrchestrator
{
    public MergeOrchestratorResult Result { get; set; } = new()
    {
        Succeeded = true,
        MergeCommitSha = "abc123def456abc123def456abc123def456abc1",
        MergeMethod = "squash",
        Summary = "Pull request #42 merged via squash.\nCommit: abc123def456abc123def456abc123def456abc1"
    };

    public Guid? LastTaskId { get; private set; }

    public Task<MergeOrchestratorResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        LastTaskId = taskId;
        return Task.FromResult(Result);
    }
}
