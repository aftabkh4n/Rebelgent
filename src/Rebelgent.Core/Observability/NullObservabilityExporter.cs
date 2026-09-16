using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Observability;

/// <summary>Default no-op <see cref="IObservabilityExporter"/> — local operation requires no
/// external or paid service.</summary>
public sealed class NullObservabilityExporter : IObservabilityExporter
{
    public Task ExportMetricsAsync(AgentMetricsSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ExportFailureAsync(ExecutionFailure failure, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
