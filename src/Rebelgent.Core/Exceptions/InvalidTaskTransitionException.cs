using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Exceptions;

/// <summary>Thrown when a requested task status transition is not permitted by the lifecycle rules.</summary>
public class InvalidTaskTransitionException : Exception
{
    public AgentTaskStatus FromStatus { get; }
    public AgentTaskStatus ToStatus { get; }

    public InvalidTaskTransitionException(AgentTaskStatus from, AgentTaskStatus to)
        : base($"Invalid task transition: {from} -> {to}.")
    {
        FromStatus = from;
        ToStatus = to;
    }
}
