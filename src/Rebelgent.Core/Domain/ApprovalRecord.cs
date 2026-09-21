namespace Rebelgent.Core.Domain;

/// <summary>
/// An immutable record of a human approval action.
/// Once created it cannot be modified or deleted.
/// </summary>
public sealed class ApprovalRecord
{
    public Guid Id { get; private set; }
    public string ActionType { get; private set; }
    public string ResourceId { get; private set; }
    public Guid HumanId { get; private set; }
    public string IdentityProvider { get; private set; }
    public string ExternalIdentityId { get; private set; }
    public DateTimeOffset ApprovedAt { get; private set; }
    public string RequestId { get; private set; }
    public Guid AuditEventId { get; private set; }

    public ApprovalRecord(
        string actionType,
        string resourceId,
        Guid humanId,
        string identityProvider,
        string externalIdentityId,
        string requestId,
        Guid auditEventId)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            throw new ArgumentException("ActionType cannot be empty.", nameof(actionType));
        if (string.IsNullOrWhiteSpace(resourceId))
            throw new ArgumentException("ResourceId cannot be empty.", nameof(resourceId));
        if (humanId == Guid.Empty)
            throw new ArgumentException("HumanId cannot be empty.", nameof(humanId));
        if (string.IsNullOrWhiteSpace(identityProvider))
            throw new ArgumentException("IdentityProvider cannot be empty.", nameof(identityProvider));
        if (string.IsNullOrWhiteSpace(externalIdentityId))
            throw new ArgumentException("ExternalIdentityId cannot be empty.", nameof(externalIdentityId));
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("RequestId cannot be empty.", nameof(requestId));

        Id = Guid.NewGuid();
        ActionType = actionType;
        ResourceId = resourceId;
        HumanId = humanId;
        IdentityProvider = identityProvider;
        ExternalIdentityId = externalIdentityId;
        ApprovedAt = DateTimeOffset.UtcNow;
        RequestId = requestId;
        AuditEventId = auditEventId;
    }

    internal static ApprovalRecord Reconstitute(
        Guid id,
        string actionType,
        string resourceId,
        Guid humanId,
        string identityProvider,
        string externalIdentityId,
        DateTimeOffset approvedAt,
        string requestId,
        Guid auditEventId)
    {
        return new ApprovalRecord
        {
            Id = id,
            ActionType = actionType,
            ResourceId = resourceId,
            HumanId = humanId,
            IdentityProvider = identityProvider,
            ExternalIdentityId = externalIdentityId,
            ApprovedAt = approvedAt,
            RequestId = requestId,
            AuditEventId = auditEventId
        };
    }

    private ApprovalRecord()
    {
        ActionType = string.Empty;
        ResourceId = string.Empty;
        IdentityProvider = string.Empty;
        ExternalIdentityId = string.Empty;
        RequestId = string.Empty;
    }
}
