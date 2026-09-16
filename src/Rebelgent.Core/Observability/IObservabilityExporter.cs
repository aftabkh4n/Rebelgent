using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Observability;

/// <summary>
/// Optional extension point for forwarding Rebelgent's own metrics and categorized failures to an
/// external observability backend (e.g. a future Langfuse or OpenTelemetry adapter project).
/// Rebelgent must remain fully functional locally without any exporter wired up — the default
/// registration is <see cref="NullObservabilityExporter"/>, a no-op. No external or paid service
/// is required by this milestone; this interface only reserves the seam for one to be added later
/// as a separate provider-specific adapter project, the same way Rebelgent.ClaudeCode/Rebelgent.GitHub
/// adapt other external providers without Rebelgent.Core depending on them directly.
/// </summary>
public interface IObservabilityExporter
{
    Task ExportMetricsAsync(AgentMetricsSnapshot snapshot, CancellationToken cancellationToken = default);

    Task ExportFailureAsync(ExecutionFailure failure, CancellationToken cancellationToken = default);
}
