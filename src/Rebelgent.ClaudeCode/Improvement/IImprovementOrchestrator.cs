using Rebelgent.Core.Domain;

namespace Rebelgent.ClaudeCode.Improvement;

/// <summary>
/// Coordinates Rebelgent's self-improvement pipeline: observe (persisted history) → analyze
/// (deterministic pattern detection) → propose (Improvement Analyst agent) → evaluate (local,
/// $0 regression check) → AwaitingApproval. A human must explicitly approve or reject every
/// proposal; approval only creates a normal <see cref="AgentTask"/> — it is never run automatically
/// and always goes through the standard Developer → build/test → QA → Reviewer → PR → merge pipeline.
/// </summary>
public interface IImprovementOrchestrator
{
    /// <summary>Analyzes persisted history and creates proposals only. Never makes code changes,
    /// never modifies agent prompts or CLAUDE.md, never creates or runs a task.</summary>
    Task<ImprovementAnalyzeResult> AnalyzeAsync(CancellationToken cancellationToken = default);

    /// <summary>Explicit human approval: creates a normal <see cref="AgentTask"/> from the
    /// proposal. Does NOT run it automatically. Idempotent — approving an already-approved
    /// proposal returns the existing result rather than creating a second task.</summary>
    Task<ImprovementDecisionResult> ApproveAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>Idempotent rejection — rejecting an already-rejected proposal is a no-op.</summary>
    Task<ImprovementDecisionResult> RejectAsync(Guid proposalId, CancellationToken cancellationToken = default);

    Task<ImprovementProposal?> GetProposalAsync(Guid proposalId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImprovementProposal>> GetProposalsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionFailure>> GetRecentFailuresAsync(int count, CancellationToken cancellationToken = default);

    Task<AgentMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken = default);
}
