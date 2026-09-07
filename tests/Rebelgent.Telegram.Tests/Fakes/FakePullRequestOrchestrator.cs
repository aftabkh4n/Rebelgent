using Rebelgent.GitHub;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakePullRequestOrchestrator : IPullRequestOrchestrator
{
    public PullRequestOrchestratorResult Result { get; set; } = new()
    {
        Succeeded = true,
        PullRequestNumber = 42,
        PullRequestUrl = "https://github.com/org/repo/pull/42",
        Summary = "Pull request created: #42 https://github.com/org/repo/pull/42"
    };

    public Guid? LastTaskId { get; private set; }

    public Task<PullRequestOrchestratorResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        LastTaskId = taskId;
        return Task.FromResult(Result);
    }
}
