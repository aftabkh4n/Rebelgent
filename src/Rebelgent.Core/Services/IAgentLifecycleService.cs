using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Services;

/// <summary>Manages agent definition lifecycle with human authorization enforcement.</summary>
public interface IAgentLifecycleService
{
    Task<AgentDefinition> ActivateAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default);
    Task<AgentDefinition> SuspendAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default);
    Task<AgentDefinition> RetireAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default);
    Task<AgentDefinition> GetAsync(Guid agentId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default);
}
