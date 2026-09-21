using Rebelgent.Core.Audit;

namespace Rebelgent.Core.Repositories;

/// <summary>
/// Append-only repository for audit events.
/// No Update or Delete methods are exposed by design.
/// </summary>
public interface IAuditRepository
{
    Task<AuditEvent> AppendAsync(AuditEvent auditEvent, CancellationToken ct = default);
    Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default);
}
