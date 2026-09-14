using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

public interface IPackageRepository
{
    Task<Package?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task AddAsync(Package package, CancellationToken cancellationToken = default);
    Task UpdateAsync(Package package, CancellationToken cancellationToken = default);
}
