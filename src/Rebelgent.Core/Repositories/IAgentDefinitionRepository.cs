using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

public interface IAgentDefinitionRepository
{
    Task<AgentDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AgentDefinition>> GetByStatusAsync(AgentLifecycleStatus status, CancellationToken ct = default);
    Task<AgentDefinition> AddAsync(AgentDefinition definition, CancellationToken ct = default);
    Task<AgentDefinition> UpdateAsync(AgentDefinition definition, CancellationToken ct = default);
}
