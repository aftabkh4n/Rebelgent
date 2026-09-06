namespace Rebelgent.Core.Domain;

/// <summary>Represents the lifecycle state of an <see cref="AgentTask"/>.</summary>
public enum AgentTaskStatus
{
    Created,
    Planning,
    AwaitingApproval,
    Approved,
    InProgress,
    Testing,
    Reviewing,
    AwaitingReview,
    ChangesRequested,
    Completed,
    Failed,
    Cancelled
}
