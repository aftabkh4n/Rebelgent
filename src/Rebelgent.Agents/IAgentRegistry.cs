using Rebelgent.Core.Domain;

namespace Rebelgent.Agents;

/// <summary>Stores and resolves <see cref="IRebelAgent"/> implementations by role.</summary>
public interface IAgentRegistry
{
    /// <summary>Registers an agent. Throws <see cref="Rebelgent.Core.Exceptions.DuplicateAgentRoleException"/> if the role is already taken.</summary>
    void Register(IRebelAgent agent);

    /// <summary>Resolves the agent for the given role. Throws <see cref="Rebelgent.Core.Exceptions.AgentNotFoundException"/> if none is registered.</summary>
    IRebelAgent Resolve(AgentRole role);

    /// <summary>Returns all currently registered agents.</summary>
    IReadOnlyCollection<IRebelAgent> GetAll();
}
