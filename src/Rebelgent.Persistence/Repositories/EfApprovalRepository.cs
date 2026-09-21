using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

/// <summary>
/// Append-only EF Core repository for approval records.
/// No Update or Delete operations are exposed.
/// </summary>
internal sealed class EfApprovalRepository : IApprovalRepository
{
    private readonly RebelgentDbContext _db;

    public EfApprovalRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task<ApprovalRecord> AddAsync(ApprovalRecord record, CancellationToken ct = default)
    {
        var dbRecord = ApprovalRecordDbRecord.FromDomain(record);
        _db.ApprovalRecords.Add(dbRecord);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<ApprovalRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await _db.ApprovalRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        return record?.ToDomain();
    }

    public async Task<ApprovalRecord?> FindAsync(string actionType, string resourceId, string humanId, CancellationToken ct = default)
    {
        var record = await _db.ApprovalRecords
            .Where(r => r.ActionType == actionType && r.ResourceId == resourceId && r.HumanId.ToString() == humanId)
            .OrderByDescending(r => r.ApprovedAt)
            .FirstOrDefaultAsync(ct);
        return record?.ToDomain();
    }

    public async Task<IReadOnlyList<ApprovalRecord>> GetForResourceAsync(string resourceId, CancellationToken ct = default)
    {
        var records = await _db.ApprovalRecords
            .Where(r => r.ResourceId == resourceId)
            .OrderByDescending(r => r.ApprovedAt)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }
}
