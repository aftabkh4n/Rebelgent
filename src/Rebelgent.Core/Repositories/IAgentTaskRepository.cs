using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

/// <summary>Provider-independent persistence boundary for <see cref="AgentTask"/>.</summary>
public interface IAgentTaskRepository
{
    Task AddAsync(AgentTask task, CancellationToken cancellationToken = default);

    Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default);

    Task<AgentTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentTask>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Returns tasks whose short ID (first 8 hex chars of the GUID) starts with <paramref name="prefix"/>.</summary>
    Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string prefix, int maxResults, CancellationToken cancellationToken = default);
}
