using Rebelgent.Orchestration.Orchestrator;
using Rebelgent.Core.Authority;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeTaskOrchestrator : ITaskOrchestrator
{
    public List<Guid> RunCalledForTaskIds { get; } = [];
    public OrchestrationResult ResultToReturn { get; set; } = new OrchestrationResult { Succeeded = true, Summary = "Done." };

    public Task<OrchestrationResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        RunCalledForTaskIds.Add(taskId);
        return Task.FromResult(ResultToReturn);
    }

    public List<Guid> RetryCalledForTaskIds { get; } = [];

    public Task<OrchestrationResult> RetryAsync(Guid taskId, HumanPrincipal human, CancellationToken cancellationToken = default)
    {
        RetryCalledForTaskIds.Add(taskId);
        return Task.FromResult(ResultToReturn);
    }
}
