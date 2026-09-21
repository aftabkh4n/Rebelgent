using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

/// <summary>
/// Append-only EF Core repository for agent versions.
/// No UpdateAsync — versions are immutable once created.
/// </summary>
internal sealed class EfAgentVersionRepository : IAgentVersionRepository
{
    private readonly RebelgentDbContext _db;

    public EfAgentVersionRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task<AgentVersion?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await _db.AgentVersions.FirstOrDefaultAsync(v => v.Id == id, ct);
        return record?.ToDomain();
    }

    public async Task<IReadOnlyList<AgentVersion>> GetForAgentAsync(Guid agentDefinitionId, CancellationToken ct = default)
    {
        var records = await _db.AgentVersions
            .Where(v => v.AgentDefinitionId == agentDefinitionId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<AgentVersion> AddAsync(AgentVersion version, CancellationToken ct = default)
    {
        var record = AgentVersionDbRecord.FromDomain(version);
        _db.AgentVersions.Add(record);
        await _db.SaveChangesAsync(ct);
        return version;
    }
}
