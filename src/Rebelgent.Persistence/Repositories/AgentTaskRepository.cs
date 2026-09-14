using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

/// <summary>SQLite-backed implementation of <see cref="IAgentTaskRepository"/>.</summary>
internal class AgentTaskRepository : IAgentTaskRepository
{
    private readonly RebelgentDbContext _db;

    public AgentTaskRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        var record = AgentTaskRecord.FromDomain(task);
        _db.AgentTasks.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        var existing = await _db.AgentTasks.FindAsync([task.Id], cancellationToken);
        if (existing is null)
            throw new InvalidOperationException($"AgentTask {task.Id} not found for update.");

        var updated = AgentTaskRecord.FromDomain(task);
        _db.Entry(existing).CurrentValues.SetValues(updated);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AgentTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await _db.AgentTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return record?.ToDomain();
    }

    public async Task<IReadOnlyCollection<AgentTask>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        var records = await _db.AgentTasks
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

        return records.Select(r => r.ToDomain()).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string prefix, int maxResults, CancellationToken cancellationToken = default)
    {
        // Load a bounded set then filter in-memory (short-ID prefix match on GUID hex string)
        var recent = await _db.AgentTasks
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var normalised = prefix.ToLowerInvariant().Replace("-", "");
        return recent
            .Where(r => r.Id.ToString("N").StartsWith(normalised, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .Select(r => r.ToDomain())
            .ToList()
            .AsReadOnly();
    }
}
