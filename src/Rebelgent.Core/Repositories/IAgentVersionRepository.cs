using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

/// <summary>
/// Repository for agent versions. Versions are immutable once created — no UpdateAsync.
/// </summary>
public interface IAgentVersionRepository
{
    Task<AgentVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentVersion>> GetForAgentAsync(Guid agentDefinitionId, CancellationToken ct = default);
    Task<AgentVersion> AddAsync(AgentVersion version, CancellationToken ct = default);
    // NO UpdateAsync — versions are immutable once created
}
