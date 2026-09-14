using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Services;

/// <summary>Application service for creating and querying agent tasks.</summary>
public interface ITaskService
{
    Task<AgentTask> CreateTaskAsync(CreateTaskInput input, CancellationToken cancellationToken = default);

    Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int count = 20, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string prefix, int maxResults, CancellationToken cancellationToken = default);

    Task<AgentTask?> TransitionAsync(Guid id, AgentTaskStatus newStatus, CancellationToken cancellationToken = default);

    Task<AgentTask?> SetBranchNameAsync(Guid id, string branchName, CancellationToken cancellationToken = default);
}
