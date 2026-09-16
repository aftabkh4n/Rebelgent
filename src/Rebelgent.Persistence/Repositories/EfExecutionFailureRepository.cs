using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

internal sealed class EfExecutionFailureRepository : IExecutionFailureRepository
{
    private readonly RebelgentDbContext _db;

    public EfExecutionFailureRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ExecutionFailure failure, CancellationToken cancellationToken = default)
    {
        var record = ExecutionFailureDbRecord.FromDomain(failure);
        _db.ExecutionFailures.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionFailure>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = await _db.ExecutionFailures
            .OrderByDescending(f => f.DetectedAt)
            .ToListAsync(cancellationToken);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<ExecutionFailure>> GetByCategoryAsync(FailureCategory category, CancellationToken cancellationToken = default)
    {
        var records = await _db.ExecutionFailures
            .Where(f => f.Category == category)
            .OrderByDescending(f => f.DetectedAt)
            .ToListAsync(cancellationToken);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public Task<bool> ExistsForExecutionAsync(Guid executionId, CancellationToken cancellationToken = default) =>
        _db.ExecutionFailures.AnyAsync(f => f.ExecutionId == executionId, cancellationToken);
}
