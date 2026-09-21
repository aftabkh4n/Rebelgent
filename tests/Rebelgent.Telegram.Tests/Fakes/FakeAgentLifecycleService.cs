using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeAgentLifecycleService : IAgentLifecycleService
{
    public List<AgentDefinition> Agents { get; set; } = [];

    public Task<AgentDefinition> ActivateAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default)
    {
        var agent = Agents.FirstOrDefault(a => a.Id == agentId)
            ?? throw new InvalidOperationException($"Agent {agentId} not found.");
        return Task.FromResult(agent);
    }

    public Task<AgentDefinition> SuspendAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default)
    {
        var agent = Agents.FirstOrDefault(a => a.Id == agentId)
            ?? throw new InvalidOperationException($"Agent {agentId} not found.");
        return Task.FromResult(agent);
    }

    public Task<AgentDefinition> RetireAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default)
    {
        var agent = Agents.FirstOrDefault(a => a.Id == agentId)
            ?? throw new InvalidOperationException($"Agent {agentId} not found.");
        return Task.FromResult(agent);
    }

    public Task<AgentDefinition> GetAsync(Guid agentId, CancellationToken ct = default)
    {
        var agent = Agents.FirstOrDefault(a => a.Id == agentId)
            ?? throw new InvalidOperationException($"Agent {agentId} not found.");
        return Task.FromResult(agent);
    }

    public Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentDefinition>>(Agents);
}
