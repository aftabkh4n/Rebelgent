namespace Rebelgent.Core.Domain;

/// <summary>Lifecycle states for a single agent code execution run.</summary>
public enum ExecutionStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled
}
