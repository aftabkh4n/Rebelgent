using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Services;

/// <summary>Analyzes system state and generates agent evolution proposals for human review.</summary>
public interface IAgentEvolutionOrchestrator
{
    Task<AgentEvolutionAnalyzeResult> AnalyzeAsync(CancellationToken ct = default);

    /// <summary>
    /// Approves an evolution proposal. Requires an authenticated <see cref="HumanPrincipal"/> with
    /// <see cref="HumanCapability.ApproveAgentEvolution"/>. Runs atomically: the proposal status
    /// change, normal implementation task, and the <c>AgentEvolutionApproved</c> audit event
    /// either all persist or none persist. The created task remains Created and is never run
    /// automatically.
    /// </summary>
    Task<AgentEvolutionProposal> ApproveAsync(Guid proposalId, HumanPrincipal human, CancellationToken ct = default);

    /// <summary>
    /// Rejects an evolution proposal. Requires an authenticated <see cref="HumanPrincipal"/> with
    /// <see cref="HumanCapability.ApproveAgentEvolution"/>. Runs atomically with its audit event.
    /// </summary>
    Task<AgentEvolutionProposal> RejectAsync(Guid proposalId, HumanPrincipal human, CancellationToken ct = default);
}

/// <summary>Result of an agent evolution analysis run.</summary>
public sealed record AgentEvolutionAnalyzeResult(
    int Detected,
    int Created,
    int Duplicates,
    int ManagerFailures,
    string Summary);
