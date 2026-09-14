using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

public interface IReleaseRepository
{
    Task<Release?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task AddAsync(Release release, CancellationToken cancellationToken = default);
    Task UpdateAsync(Release release, CancellationToken cancellationToken = default);
}
