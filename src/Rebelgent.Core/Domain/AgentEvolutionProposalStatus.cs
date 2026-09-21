namespace Rebelgent.Core.Domain;

public enum AgentEvolutionProposalStatus
{
    Proposed = 1,
    Evaluating = 2,
    AwaitingApproval = 3,
    Approved = 4,
    Rejected = 5,
    Implemented = 6,
    Failed = 7
}
