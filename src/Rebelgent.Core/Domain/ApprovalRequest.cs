namespace Rebelgent.Core.Domain;

/// <summary>
/// Records a request for human approval before a sensitive action may proceed.
/// Approvals will eventually be delivered and resolved via Telegram.
/// </summary>
public class ApprovalRequest
{
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public ApprovalType Type { get; private set; }
    public string Description { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public ApprovalDecision? Decision { get; private set; }

    public bool IsResolved => Decision.HasValue;

    public ApprovalRequest(Guid taskId, ApprovalType type, string description)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty.", nameof(description));

        Id = Guid.NewGuid();
        TaskId = taskId;
        Type = type;
        Description = description;
        RequestedAt = DateTimeOffset.UtcNow;
    }

    public void Resolve(ApprovalDecision decision)
    {
        if (IsResolved)
            throw new InvalidOperationException("Approval request has already been resolved.");

        Decision = decision;
        ResolvedAt = DateTimeOffset.UtcNow;
    }
}
