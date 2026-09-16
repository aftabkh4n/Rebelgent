using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Core.Tests.Services.Fakes;

internal sealed class FakeAgentTaskRepository : IAgentTaskRepository
{
    public List<AgentTask> Tasks { get; } = [];

    public Task AddAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        Tasks.Add(task);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<AgentTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tasks.FirstOrDefault(t => t.Id == id));

    public Task<IReadOnlyCollection<AgentTask>> GetRecentAsync(int count, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<AgentTask>>(Tasks.Take(count).ToList());

    public Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string prefix, int maxResults, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<AgentTask>>(
            Tasks.Where(t => t.Id.ToString("N").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(maxResults).ToList());
}
