using Rebelgent.Core.Domain;
using Rebelgent.Core.Exceptions;

namespace Rebelgent.Agents;

/// <summary>Thread-safe in-memory registry of <see cref="IRebelAgent"/> implementations.</summary>
public class AgentRegistry : IAgentRegistry
{
    private readonly Dictionary<AgentRole, IRebelAgent> _agents = [];
    private readonly object _lock = new();

    public void Register(IRebelAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        lock (_lock)
        {
            if (_agents.ContainsKey(agent.Role))
                throw new DuplicateAgentRoleException(agent.Role);

            _agents[agent.Role] = agent;
        }
    }

    public IRebelAgent Resolve(AgentRole role)
    {
        lock (_lock)
        {
            if (!_agents.TryGetValue(role, out var agent))
                throw new AgentNotFoundException(role);

            return agent;
        }
    }

    public IReadOnlyCollection<IRebelAgent> GetAll()
    {
        lock (_lock)
        {
            return _agents.Values.ToList().AsReadOnly();
        }
    }
}
