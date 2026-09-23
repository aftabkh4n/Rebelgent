namespace Rebelgent.Core.Audit;

/// <summary>Well-known audit event type string constants.</summary>
public static class AuditEventType
{
    public const string TaskCreated = "TaskCreated";
    public const string TaskStarted = "TaskStarted";
    public const string TaskCompleted = "TaskCompleted";
    public const string TaskRetryStarted = "TaskRetryStarted";
    public const string TaskRetryPreflightFailed = "TaskRetryPreflightFailed";
    public const string TaskRetryRequested = "TaskRetryRequested";
    public const string ExecutionFailed = "ExecutionFailed";
    public const string ImprovementProposalCreated = "ImprovementProposalCreated";
    public const string ImprovementProposalApproved = "ImprovementProposalApproved";
    public const string AgentEvolutionProposed = "AgentEvolutionProposed";
    public const string AgentCreated = "AgentCreated";
    public const string AgentVersionCreated = "AgentVersionCreated";
    public const string AgentActivated = "AgentActivated";
    public const string AgentSuspended = "AgentSuspended";
    public const string AgentRetired = "AgentRetired";
    public const string HumanApprovalGranted = "HumanApprovalGranted";
    public const string UnauthorizedApprovalAttempt = "UnauthorizedApprovalAttempt";
    public const string HumanImpersonationAttempt = "HumanImpersonationAttempt";
    public const string AgentPrivilegeEscalationAttempt = "AgentPrivilegeEscalationAttempt";
    public const string SecurityPolicyChangeAttempt = "SecurityPolicyChangeAttempt";
    public const string AuditMutationAttempt = "AuditMutationAttempt";
    public const string AgentDeletionAttempt = "AgentDeletionAttempt";
    public const string ApprovalReplayAttempt = "ApprovalReplayAttempt";
    public const string AuditCorrectionAdded = "AuditCorrectionAdded";
    public const string AgentEvolutionApproved = "AgentEvolutionApproved";
    public const string AgentEvolutionRejected = "AgentEvolutionRejected";
    public const string LegacyEvolutionTargetBackfilled = "LegacyEvolutionTargetBackfilled";
    public const string EvolutionImplementationTaskCreated = "EvolutionImplementationTaskCreated";
    public const string BuiltInAgentImported = "BuiltInAgentImported";
}
