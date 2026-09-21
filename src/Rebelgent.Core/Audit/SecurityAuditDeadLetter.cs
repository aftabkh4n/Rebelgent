using Rebelgent.Core.Authority;

namespace Rebelgent.Core.Audit;

/// <summary>
/// Durable fallback record for a security-critical audit event whose primary
/// audit-ledger append failed. Append-only. Never edited or deleted.
/// Recovery is tracked via a separate append-only <see cref="SecurityAuditDeadLetterRecovery"/>.
/// </summary>
public sealed class SecurityAuditDeadLetter
{
    public Guid Id { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }
    public string EventType { get; private set; }
    public ActorType ActorType { get; private set; }
    public string ActorId { get; private set; }
    public string ResourceType { get; private set; }
    public string ResourceId { get; private set; }
    public string Action { get; private set; }
    public string PayloadJson { get; private set; }
    public string PrimaryAuditError { get; private set; }

    public SecurityAuditDeadLetter(
        DateTimeOffset timestampUtc,
        string eventType,
        ActorType actorType,
        string actorId,
        string resourceType,
        string resourceId,
        string action,
        string payloadJson,
        string primaryAuditError)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType cannot be empty.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("ActorId cannot be empty.", nameof(actorId));
        ArgumentNullException.ThrowIfNull(payloadJson);
        ArgumentNullException.ThrowIfNull(primaryAuditError);

        Id = Guid.NewGuid();
        TimestampUtc = timestampUtc;
        EventType = eventType;
        ActorType = actorType;
        ActorId = actorId;
        ResourceType = resourceType ?? string.Empty;
        ResourceId = resourceId ?? string.Empty;
        Action = action ?? string.Empty;
        PayloadJson = payloadJson;
        PrimaryAuditError = primaryAuditError;
    }

    internal static SecurityAuditDeadLetter Reconstitute(
        Guid id, DateTimeOffset timestampUtc, string eventType, ActorType actorType,
        string actorId, string resourceType, string resourceId, string action,
        string payloadJson, string primaryAuditError)
    {
        return new SecurityAuditDeadLetter
        {
            Id = id,
            TimestampUtc = timestampUtc,
            EventType = eventType,
            ActorType = actorType,
            ActorId = actorId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            PayloadJson = payloadJson,
            PrimaryAuditError = primaryAuditError
        };
    }

    private SecurityAuditDeadLetter()
    {
        EventType = string.Empty;
        ActorId = string.Empty;
        ResourceType = string.Empty;
        ResourceId = string.Empty;
        Action = string.Empty;
        PayloadJson = string.Empty;
        PrimaryAuditError = string.Empty;
    }
}
