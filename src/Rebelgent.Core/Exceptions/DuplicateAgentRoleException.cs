using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Exceptions;

/// <summary>Thrown when an attempt is made to register a second agent for a role that already has one.</summary>
public class DuplicateAgentRoleException : Exception
{
    public AgentRole Role { get; }

    public DuplicateAgentRoleException(AgentRole role)
        : base($"An agent for role '{role}' is already registered.")
    {
        Role = role;
    }
}
