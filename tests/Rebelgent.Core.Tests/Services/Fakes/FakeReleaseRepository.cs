using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Core.Tests.Services.Fakes;

internal sealed class FakeReleaseRepository : IReleaseRepository
{
    public List<Release> Releases { get; } = [];

    public Task<Release?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Releases.FirstOrDefault(r => r.TaskId == taskId));

    public Task AddAsync(Release release, CancellationToken cancellationToken = default)
    {
        Releases.Add(release);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Release release, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<Release>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Release>>(Releases);
}
