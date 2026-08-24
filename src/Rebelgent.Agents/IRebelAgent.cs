using Rebelgent.Core.Domain;

namespace Rebelgent.Agents;

/// <summary>
/// The core abstraction for all Rebelgent agents.
/// Implementations must be provider-independent at this interface boundary;
/// provider-specific behaviour belongs in adapter projects.
/// </summary>
public interface IRebelAgent
{
    /// <summary>Display name of the agent.</summary>
    string Name { get; }

    /// <summary>The role this agent fulfills within the software house.</summary>
    AgentRole Role { get; }

    /// <summary>Executes the agent against the given task.</summary>
    Task<AgentExecutionResult> ExecuteAsync(
        AgentTask task,
        AgentExecutionContext context,
        CancellationToken cancellationToken);
}
