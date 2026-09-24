namespace Rebelgent.Core.Services;

/// <summary>
/// Derives evolution-proposal lifecycle state from persisted, governed evidence:
/// <list type="bullet">
/// <item>Repairs a persisted-only gap where an approved proposal's <c>TargetProjectId</c>
/// was never written to storage but is available from the configured registered project.</item>
/// <item>Transitions <c>Approved</c> proposals whose linked <c>AgentTask</c> carries a
/// verified <c>MergeCommitSha</c> to <c>Implemented</c>, and appends the
/// <c>AgentEvolutionImplemented</c> audit event exactly once.</item>
/// </list>
/// The reconciler never grants human authority, never accepts model-supplied text as
/// implementation proof, and is fully idempotent — a second run produces no additional
/// mutations and no additional audit events.
/// </summary>
public interface IEvolutionLifecycleReconciler
{
    Task<EvolutionLifecycleReconciliationResult> ReconcileAsync(CancellationToken ct = default);
}

/// <summary>Outcome of a reconciliation invocation.</summary>
public sealed record EvolutionLifecycleReconciliationResult(
    int TargetProjectsRepaired,
    int ProposalsMarkedImplemented,
    int ProposalsSkipped);
