namespace Rebelgent.Core.Domain;

/// <summary>Lifecycle status of an agent definition. There is deliberately no Deleted state.</summary>
public enum AgentLifecycleStatus
{
    Draft = 1,
    Proposed = 2,
    Evaluating = 3,
    AwaitingApproval = 4,
    Active = 5,
    Suspended = 6,
    Retired = 7
}
