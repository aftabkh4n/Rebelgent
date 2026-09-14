using Rebelgent.Orchestration.Orchestrator;

namespace Rebelgent.Telegram.Tests.Fakes;

internal sealed class FakeQualityOrchestrator : IQualityOrchestrator
{
    public List<Guid> Calls { get; } = [];
    public QualityOrchestrationResult Result { get; set; } = new() { Succeeded = true, Summary = "QA and Review complete." };

    public Task<QualityOrchestrationResult> RunAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        Calls.Add(taskId);
        return Task.FromResult(Result);
    }
}
