using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

internal sealed class EfPackageRepository : IPackageRepository
{
    private readonly RebelgentDbContext _db;

    public EfPackageRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task<Package?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var record = await _db.Packages
            .Where(p => p.TaskId == taskId)
            .OrderByDescending(p => p.PreparedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return record?.ToDomain();
    }

    public async Task AddAsync(Package package, CancellationToken cancellationToken = default)
    {
        // TaskId is unique per package (one package per task): a retry after a Failed
        // attempt replaces the old row instead of violating the unique index on TaskId.
        var existing = await _db.Packages
            .FirstOrDefaultAsync(p => p.TaskId == package.TaskId, cancellationToken);
        if (existing is not null)
            _db.Packages.Remove(existing);

        var record = PackageDbRecord.FromDomain(package);
        _db.Packages.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Package package, CancellationToken cancellationToken = default)
    {
        var record = await _db.Packages
            .FirstOrDefaultAsync(p => p.Id == package.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Package {package.Id} not found in database.");

        record.Status = package.Status;
        record.PublishedAt = package.PublishedAt?.UtcTicks;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Package>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = await _db.Packages.ToListAsync(cancellationToken);
        return records.Select(r => r.ToDomain()).ToList();
    }
}
