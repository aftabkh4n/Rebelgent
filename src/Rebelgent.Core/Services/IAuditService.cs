using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;

namespace Rebelgent.Core.Services;

/// <summary>Records tamper-evident audit events to the append-only ledger.</summary>
public interface IAuditService
{
    Task<AuditEvent> RecordAsync(
        string eventType,
        ActorType actorType,
        string actorId,
        string resourceType,
        string resourceId,
        string action,
        object? payload = null,
        CancellationToken ct = default);

    Task<AuditEvent> RecordSecurityEventAsync(
        string eventType,
        ActorType actorType,
        string actorId,
        string action,
        string details,
        CancellationToken ct = default);
}
