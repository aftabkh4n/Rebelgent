using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Exceptions;

/// <summary>Thrown when no agent is registered for the requested role.</summary>
public class AgentNotFoundException : Exception
{
    public AgentRole Role { get; }

    public AgentNotFoundException(AgentRole role)
        : base($"No agent is registered for role '{role}'.")
    {
        Role = role;
    }
}
