using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Services;

/// <summary>Computes Rebelgent's own operational metrics on demand from persisted history.
/// Provider-independent — pure aggregation over Core repositories, no LLM calls.</summary>
public interface IMetricsCalculator
{
    Task<AgentMetricsSnapshot> ComputeAsync(CancellationToken cancellationToken = default);
}
