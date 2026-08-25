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
}
