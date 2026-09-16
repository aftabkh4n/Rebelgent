using Rebelgent.Core.Domain;
using Rebelgent.Core.Observability;

namespace Rebelgent.Core.Tests.Services.Fakes;

internal sealed class FakeObservabilityExporter : IObservabilityExporter
{
    public List<ExecutionFailure> ExportedFailures { get; } = [];
    public List<AgentMetricsSnapshot> ExportedMetrics { get; } = [];

    public Task ExportMetricsAsync(AgentMetricsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ExportedMetrics.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task ExportFailureAsync(ExecutionFailure failure, CancellationToken cancellationToken = default)
    {
        ExportedFailures.Add(failure);
        return Task.CompletedTask;
    }
}
