using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;

namespace Rebelgent.Persistence.Records;

internal class SecurityAuditDeadLetterDbRecord
{
    public Guid Id { get; set; }
    public long TimestampUtc { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int ActorType { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string PrimaryAuditError { get; set; } = string.Empty;

    public static SecurityAuditDeadLetterDbRecord FromDomain(SecurityAuditDeadLetter e) => new()
    {
        Id = e.Id,
        TimestampUtc = e.TimestampUtc.UtcTicks,
        EventType = e.EventType,
        ActorType = (int)e.ActorType,
        ActorId = e.ActorId,
        ResourceType = e.ResourceType,
        ResourceId = e.ResourceId,
        Action = e.Action,
        PayloadJson = e.PayloadJson,
        PrimaryAuditError = e.PrimaryAuditError
    };

    public SecurityAuditDeadLetter ToDomain() => SecurityAuditDeadLetter.Reconstitute(
        Id,
        new DateTimeOffset(TimestampUtc, TimeSpan.Zero),
        EventType,
        (ActorType)ActorType,
        ActorId,
        ResourceType,
        ResourceId,
        Action,
        PayloadJson,
        PrimaryAuditError);
}

internal class SecurityAuditDeadLetterRecoveryDbRecord
{
    public Guid Id { get; set; }
    public Guid DeadLetterId { get; set; }
    public Guid RecoveredAuditEventId { get; set; }
    public long RecoveredAt { get; set; }
    public Guid RecoveredByHumanId { get; set; }

    public static SecurityAuditDeadLetterRecoveryDbRecord FromDomain(SecurityAuditDeadLetterRecovery r) => new()
    {
        Id = r.Id,
        DeadLetterId = r.DeadLetterId,
        RecoveredAuditEventId = r.RecoveredAuditEventId,
        RecoveredAt = r.RecoveredAt.UtcTicks,
        RecoveredByHumanId = r.RecoveredByHumanId
    };

    public SecurityAuditDeadLetterRecovery ToDomain() => SecurityAuditDeadLetterRecovery.Reconstitute(
        Id, DeadLetterId, RecoveredAuditEventId,
        new DateTimeOffset(RecoveredAt, TimeSpan.Zero),
        RecoveredByHumanId);
}
