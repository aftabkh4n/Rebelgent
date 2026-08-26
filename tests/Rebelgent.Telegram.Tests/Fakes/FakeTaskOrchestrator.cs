using Rebelgent.Orchestration.Orchestrator;

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
}
