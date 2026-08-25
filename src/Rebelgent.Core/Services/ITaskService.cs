using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Services;

/// <summary>Application service for creating and querying agent tasks.</summary>
public interface ITaskService
{
    Task<AgentTask> CreateTaskAsync(CreateTaskInput input, CancellationToken cancellationToken = default);

    Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int count = 20, CancellationToken cancellationToken = default);
}
