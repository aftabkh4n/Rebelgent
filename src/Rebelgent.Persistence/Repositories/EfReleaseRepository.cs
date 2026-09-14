using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

internal sealed class EfReleaseRepository : IReleaseRepository
{
    private readonly RebelgentDbContext _db;

    public EfReleaseRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task<Release?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var record = await _db.Releases
            .FirstOrDefaultAsync(r => r.TaskId == taskId, cancellationToken);
        return record?.ToDomain();
    }

    public async Task AddAsync(Release release, CancellationToken cancellationToken = default)
    {
        var record = ReleaseDbRecord.FromDomain(release);
        _db.Releases.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Release release, CancellationToken cancellationToken = default)
    {
        var record = await _db.Releases
            .FirstOrDefaultAsync(r => r.Id == release.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Release {release.Id} not found in database.");

        record.Status = release.Status;
        record.GitHubReleaseUrl = release.GitHubReleaseUrl;
        record.PublishedAt = release.PublishedAt?.UtcTicks;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
