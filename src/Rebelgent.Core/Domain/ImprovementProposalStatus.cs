namespace Rebelgent.Core.Domain;

/// <summary>Lifecycle states for an <see cref="ImprovementProposal"/>.</summary>
public enum ImprovementProposalStatus
{
    Proposed,
    Evaluating,
    AwaitingApproval,
    Approved,
    Rejected,
    Implemented,
    Failed
}
