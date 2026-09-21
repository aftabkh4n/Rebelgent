using Rebelgent.Core.Authority;

namespace Rebelgent.Core.Services;

/// <summary>
/// Recovers <see cref="Rebelgent.Core.Audit.SecurityAuditDeadLetter"/> entries into the canonical
/// audit ledger. RECOVERY ONLY REPAIRS AUDIT RECORDS. It never replays the denied privileged
/// operation, never approves anything, and never changes application state other than audit
/// metadata (a new <see cref="Rebelgent.Core.Audit.AuditEvent"/> row plus one recovery-linkage row).
/// </summary>
public interface IAuditRecoveryService
{
    Task<AuditRecoveryResult> RecoverAsync(Guid deadLetterId, HumanPrincipal human, CancellationToken ct = default);

    Task<AuditRecoveryBatchResult> RecoverAllUnresolvedAsync(HumanPrincipal human, CancellationToken ct = default);
}

/// <summary>Outcome of recovering a single dead-letter entry.</summary>
public sealed record AuditRecoveryResult(
    bool Succeeded,
    Guid DeadLetterId,
    Guid? RecoveredAuditEventId,
    bool WasAlreadyResolved,
    string? Reason);

/// <summary>Outcome of a batch recovery run.</summary>
public sealed record AuditRecoveryBatchResult(
    int Attempted,
    int RecoveredNow,
    int AlreadyResolved,
    int Failed);
