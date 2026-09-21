namespace Rebelgent.Core.Authority;

public enum AuthorityViolationKind
{
    AgentApprovalAttempt,
    AgentActivationAttempt,
    AgentSuspensionAttempt,
    AgentRetirementAttempt,
    HumanImpersonationAttempt,
    CapabilityMissing,
    UnauthorizedApprovalAttempt,
    PrivilegeEscalationAttempt,
    AuditMutationAttempt,
    AgentDeletionAttempt,
    ApprovalReplayAttempt
}
