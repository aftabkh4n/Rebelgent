using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

/// <summary>Provider-independent persistence boundary for <see cref="AgentExecutionRecord"/>.</summary>
public interface IAgentExecutionRepository
{
    Task AddAsync(AgentExecutionRecord record, CancellationToken cancellationToken = default);

    Task UpdateAsync(AgentExecutionRecord record, CancellationToken cancellationToken = default);

    Task<AgentExecutionRecord?> GetLatestByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<AgentExecutionRecord?> GetLatestByTaskIdAndRoleAsync(Guid taskId, AgentRole role, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentExecutionRecord>> GetAllByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);
}
