using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeTaskService : ITaskService
{
    public List<AgentTask> CreatedTasks { get; } = [];

    public Task<AgentTask> CreateTaskAsync(CreateTaskInput input, CancellationToken cancellationToken = default)
    {
        var task = new AgentTask(input.ProjectId, input.Title, input.Description, AgentRole.EngineeringManager, RiskLevel.Medium);
        CreatedTasks.Add(task);
        return Task.FromResult(task);
    }

    public Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(CreatedTasks.FirstOrDefault(t => t.Id == id));

    public Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<AgentTask> result = CreatedTasks.TakeLast(count).ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string prefix, int maxResults, CancellationToken cancellationToken = default)
    {
        var normalised = prefix.ToLowerInvariant().Replace("-", "");
        IReadOnlyCollection<AgentTask> result = CreatedTasks
            .Where(t => t.Id.ToString("N").StartsWith(normalised, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToList()
            .AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<AgentTask?> TransitionAsync(Guid id, AgentTaskStatus newStatus, CancellationToken cancellationToken = default)
        => Task.FromResult(CreatedTasks.FirstOrDefault(t => t.Id == id));

    public Task<AgentTask?> SetBranchNameAsync(Guid id, string branchName, CancellationToken cancellationToken = default)
        => Task.FromResult(CreatedTasks.FirstOrDefault(t => t.Id == id));
}
