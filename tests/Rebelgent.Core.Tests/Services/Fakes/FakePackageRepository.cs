using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Core.Tests.Services.Fakes;

internal sealed class FakePackageRepository : IPackageRepository
{
    public List<Package> Packages { get; } = [];

    public Task<Package?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Packages.FirstOrDefault(p => p.TaskId == taskId));

    public Task AddAsync(Package package, CancellationToken cancellationToken = default)
    {
        Packages.Add(package);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Package package, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<Package>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Package>>(Packages);
}
