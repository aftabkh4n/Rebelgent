using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

/// <summary>SQLite-backed implementation of <see cref="IAgentExecutionRepository"/>.</summary>
internal class AgentExecutionRepository : IAgentExecutionRepository
{
    private readonly RebelgentDbContext _db;

    public AgentExecutionRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AgentExecutionRecord record, CancellationToken cancellationToken = default)
    {
        var dbRecord = AgentExecutionDbRecord.FromDomain(record);
        _db.AgentExecutions.Add(dbRecord);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AgentExecutionRecord record, CancellationToken cancellationToken = default)
    {
        var existing = await _db.AgentExecutions.FindAsync([record.Id], cancellationToken);
        if (existing is null)
            throw new InvalidOperationException($"AgentExecutionRecord {record.Id} not found for update.");

        var updated = AgentExecutionDbRecord.FromDomain(record);
        _db.Entry(existing).CurrentValues.SetValues(updated);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AgentExecutionRecord?> GetLatestByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var record = await _db.AgentExecutions
            .AsNoTracking()
            .Where(e => e.TaskId == taskId)
            .OrderByDescending(e => e.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return record?.ToDomain();
    }

    public async Task<AgentExecutionRecord?> GetLatestByTaskIdAndRoleAsync(Guid taskId, AgentRole role, CancellationToken cancellationToken = default)
    {
        var roleInt = (int)role;
        var record = await _db.AgentExecutions
            .AsNoTracking()
            .Where(e => e.TaskId == taskId && e.Role == roleInt)
            .OrderByDescending(e => e.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return record?.ToDomain();
    }

    public async Task<IReadOnlyList<AgentExecutionRecord>> GetAllByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var records = await _db.AgentExecutions
            .AsNoTracking()
            .Where(e => e.TaskId == taskId)
            .OrderBy(e => e.StartedAt)
            .ToListAsync(cancellationToken);

        return records.Select(r => r.ToDomain()).ToList();
    }
}
