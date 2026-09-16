using Rebelgent.Core.Domain;
using Rebelgent.Core.Observability;

namespace Rebelgent.Core.Tests.Observability;

public class NullObservabilityExporterTests
{
    [Fact]
    public async Task ExportMetricsAsync_NeverThrows()
    {
        var exporter = new NullObservabilityExporter();
        var snapshot = new AgentMetricsSnapshot(0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<FailureCategory, int>());

        await exporter.ExportMetricsAsync(snapshot);
    }

    [Fact]
    public async Task ExportFailureAsync_NeverThrows()
    {
        var exporter = new NullObservabilityExporter();
        var failure = new ExecutionFailure(Guid.NewGuid(), null, FailureCategory.BuildFailure, "BackendDeveloper", "message");

        await exporter.ExportFailureAsync(failure);
    }
}
