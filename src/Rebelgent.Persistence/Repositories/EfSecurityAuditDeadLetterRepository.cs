using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

/// <summary>
/// Append-only EF Core repository for security-audit dead-letter entries and their recovery
/// linkage. No Update or Delete methods are exposed. Both tables are further protected at the
/// SQLite level by BEFORE UPDATE / BEFORE DELETE triggers installed by the AddSecurityAuditDeadLetter
/// migration.
/// </summary>
internal sealed class EfSecurityAuditDeadLetterRepository : ISecurityAuditDeadLetterRepository
{
    private readonly RebelgentDbContext _db;

    public EfSecurityAuditDeadLetterRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task<SecurityAuditDeadLetter> AppendAsync(SecurityAuditDeadLetter entry, CancellationToken ct = default)
    {
        _db.SecurityAuditDeadLetters.Add(SecurityAuditDeadLetterDbRecord.FromDomain(entry));
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<SecurityAuditDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await _db.SecurityAuditDeadLetters.FirstOrDefaultAsync(x => x.Id == id, ct);
        return record?.ToDomain();
    }

    public async Task<IReadOnlyList<SecurityAuditDeadLetter>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await _db.SecurityAuditDeadLetters
            .OrderBy(x => x.TimestampUtc)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<SecurityAuditDeadLetter>> GetUnresolvedAsync(CancellationToken ct = default)
    {
        // Unresolved = no row in SecurityAuditDeadLetterRecoveries references this DeadLetter.Id.
        var resolvedIds = _db.SecurityAuditDeadLetterRecoveries.Select(r => r.DeadLetterId);
        var records = await _db.SecurityAuditDeadLetters
            .Where(d => !resolvedIds.Contains(d.Id))
            .OrderBy(d => d.TimestampUtc)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<int> CountUnresolvedAsync(CancellationToken ct = default)
    {
        var resolvedIds = _db.SecurityAuditDeadLetterRecoveries.Select(r => r.DeadLetterId);
        return await _db.SecurityAuditDeadLetters
            .Where(d => !resolvedIds.Contains(d.Id))
            .CountAsync(ct);
    }

    public async Task<SecurityAuditDeadLetterRecovery> AppendRecoveryAsync(SecurityAuditDeadLetterRecovery recovery, CancellationToken ct = default)
    {
        _db.SecurityAuditDeadLetterRecoveries.Add(SecurityAuditDeadLetterRecoveryDbRecord.FromDomain(recovery));
        await _db.SaveChangesAsync(ct);
        return recovery;
    }

    public async Task<IReadOnlyList<SecurityAuditDeadLetterRecovery>> GetRecoveriesAsync(Guid deadLetterId, CancellationToken ct = default)
    {
        var records = await _db.SecurityAuditDeadLetterRecoveries
            .Where(r => r.DeadLetterId == deadLetterId)
            .OrderBy(r => r.RecoveredAt)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }
}
