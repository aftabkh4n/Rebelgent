namespace Rebelgent.Core.Audit;

/// <summary>
/// Append-only linkage record produced when a <see cref="SecurityAuditDeadLetter"/> is recovered
/// into the canonical <see cref="AuditEvent"/> ledger. The original dead-letter row is never
/// modified — recovery is expressed by adding one of these rows referencing it.
/// </summary>
public sealed class SecurityAuditDeadLetterRecovery
{
    public Guid Id { get; private set; }
    public Guid DeadLetterId { get; private set; }
    public Guid RecoveredAuditEventId { get; private set; }
    public DateTimeOffset RecoveredAt { get; private set; }
    public Guid RecoveredByHumanId { get; private set; }

    public SecurityAuditDeadLetterRecovery(
        Guid deadLetterId,
        Guid recoveredAuditEventId,
        Guid recoveredByHumanId)
    {
        if (deadLetterId == Guid.Empty)
            throw new ArgumentException("DeadLetterId cannot be empty.", nameof(deadLetterId));
        if (recoveredAuditEventId == Guid.Empty)
            throw new ArgumentException("RecoveredAuditEventId cannot be empty.", nameof(recoveredAuditEventId));
        if (recoveredByHumanId == Guid.Empty)
            throw new ArgumentException("RecoveredByHumanId cannot be empty.", nameof(recoveredByHumanId));

        Id = Guid.NewGuid();
        DeadLetterId = deadLetterId;
        RecoveredAuditEventId = recoveredAuditEventId;
        RecoveredByHumanId = recoveredByHumanId;
        RecoveredAt = DateTimeOffset.UtcNow;
    }

    internal static SecurityAuditDeadLetterRecovery Reconstitute(
        Guid id, Guid deadLetterId, Guid recoveredAuditEventId,
        DateTimeOffset recoveredAt, Guid recoveredByHumanId)
    {
        return new SecurityAuditDeadLetterRecovery
        {
            Id = id,
            DeadLetterId = deadLetterId,
            RecoveredAuditEventId = recoveredAuditEventId,
            RecoveredAt = recoveredAt,
            RecoveredByHumanId = recoveredByHumanId
        };
    }

    private SecurityAuditDeadLetterRecovery() { }
}
