using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="AuditEvent"/>.</summary>
internal class AuditEventDbRecord
{
    public Guid Id { get; set; }
    public long SequenceNumber { get; set; }
    public long TimestampUtc { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int ActorType { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string PreviousHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    public static AuditEventDbRecord FromDomain(AuditEvent e) => new()
    {
        Id = e.Id,
        SequenceNumber = e.SequenceNumber,
        TimestampUtc = e.TimestampUtc.UtcTicks,
        EventType = e.EventType,
        ActorType = (int)e.ActorType,
        ActorId = e.ActorId,
        ResourceType = e.ResourceType,
        ResourceId = e.ResourceId,
        Action = e.Action,
        PayloadJson = e.PayloadJson,
        PreviousHash = e.PreviousHash,
        Hash = e.Hash
    };

    public AuditEvent ToDomain() => AuditEvent.Reconstitute(
        Id,
        SequenceNumber,
        new DateTimeOffset(TimestampUtc, TimeSpan.Zero),
        EventType,
        (ActorType)ActorType,
        ActorId,
        ResourceType,
        ResourceId,
        Action,
        PayloadJson,
        PreviousHash,
        Hash);
}
