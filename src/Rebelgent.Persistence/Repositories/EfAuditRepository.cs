using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

/// <summary>
/// Append-only EF Core repository for audit events.
/// No Update or Delete operations are exposed.
/// </summary>
internal sealed class EfAuditRepository : IAuditRepository
{
    private readonly RebelgentDbContext _db;

    public EfAuditRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task<AuditEvent> AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        var record = AuditEventDbRecord.FromDomain(auditEvent);
        _db.AuditEvents.Add(record);
        await _db.SaveChangesAsync(ct);
        return auditEvent;
    }

    public async Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default)
    {
        var record = await _db.AuditEvents
            .OrderByDescending(e => e.SequenceNumber)
            .FirstOrDefaultAsync(ct);
        return record?.ToDomain();
    }

    public async Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default)
    {
        var records = await _db.AuditEvents
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        var records = await _db.AuditEvents
            .OrderByDescending(e => e.SequenceNumber)
            .Take(count)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }
}
