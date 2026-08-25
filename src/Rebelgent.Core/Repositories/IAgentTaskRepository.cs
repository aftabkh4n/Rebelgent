using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

/// <summary>Provider-independent persistence boundary for <see cref="AgentTask"/>.</summary>
public interface IAgentTaskRepository
{
    Task AddAsync(AgentTask task, CancellationToken cancellationToken = default);

    Task<AgentTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentTask>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
}
