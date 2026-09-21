using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

internal sealed class EfAgentDefinitionRepository : IAgentDefinitionRepository
{
    private readonly RebelgentDbContext _db;

    public EfAgentDefinitionRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task<AgentDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await _db.AgentDefinitions.FirstOrDefaultAsync(a => a.Id == id, ct);
        return record?.ToDomain();
    }

    public async Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await _db.AgentDefinitions.OrderBy(a => a.Name).ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<AgentDefinition>> GetByStatusAsync(AgentLifecycleStatus status, CancellationToken ct = default)
    {
        var statusInt = (int)status;
        var records = await _db.AgentDefinitions
            .Where(a => a.Status == statusInt)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<AgentDefinition> AddAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var record = AgentDefinitionDbRecord.FromDomain(definition);
        _db.AgentDefinitions.Add(record);
        await _db.SaveChangesAsync(ct);
        return definition;
    }

    public async Task<AgentDefinition> UpdateAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var record = await _db.AgentDefinitions
            .FirstOrDefaultAsync(a => a.Id == definition.Id, ct)
            ?? throw new InvalidOperationException($"AgentDefinition {definition.Id} not found in database.");

        record.Status = (int)definition.Status;
        record.CurrentVersionId = definition.CurrentVersionId;
        record.ActivatedAt = definition.ActivatedAt?.UtcTicks;
        record.SuspendedAt = definition.SuspendedAt?.UtcTicks;
        record.RetiredAt = definition.RetiredAt?.UtcTicks;

        await _db.SaveChangesAsync(ct);
        return definition;
    }
}
