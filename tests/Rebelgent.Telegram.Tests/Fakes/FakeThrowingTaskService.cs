using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;

namespace Rebelgent.Telegram.Tests.Fakes;

/// <summary>Simulates a persistence failure on read operations.</summary>
internal class FakeThrowingTaskService : ITaskService
{
    public Task<AgentTask> CreateTaskAsync(CreateTaskInput input, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated persistence failure.");

    public Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated persistence failure.");

    public Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int count = 20, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated persistence failure.");

    public Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string prefix, int maxResults, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated persistence failure.");

    public Task<AgentTask?> TransitionAsync(Guid id, AgentTaskStatus newStatus, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated persistence failure.");

    public Task<AgentTask?> SetBranchNameAsync(Guid id, string branchName, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated persistence failure.");
}
