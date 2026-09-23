using Rebelgent.Core.Domain;
using Rebelgent.Core.Exceptions;

namespace Rebelgent.Core.Services;

/// <summary>
/// Governs valid status transitions for <see cref="AgentTask"/>.
/// Agents may not set task statuses directly; all transitions must go through this service.
/// </summary>
public class TaskLifecycleService
{
    private static readonly Dictionary<AgentTaskStatus, HashSet<AgentTaskStatus>> ValidTransitions = new()
    {
        [AgentTaskStatus.Created] = [AgentTaskStatus.Planning, AgentTaskStatus.Cancelled, AgentTaskStatus.Failed],
        [AgentTaskStatus.Planning] = [AgentTaskStatus.AwaitingApproval, AgentTaskStatus.Cancelled, AgentTaskStatus.Failed],
        [AgentTaskStatus.AwaitingApproval] = [AgentTaskStatus.Approved, AgentTaskStatus.Cancelled, AgentTaskStatus.Failed],
        [AgentTaskStatus.Approved] = [AgentTaskStatus.InProgress, AgentTaskStatus.Cancelled, AgentTaskStatus.Failed],
        [AgentTaskStatus.InProgress] = [AgentTaskStatus.Testing, AgentTaskStatus.Cancelled, AgentTaskStatus.Failed],
        [AgentTaskStatus.Testing] = [AgentTaskStatus.Reviewing, AgentTaskStatus.ChangesRequested, AgentTaskStatus.Failed, AgentTaskStatus.Cancelled],
        [AgentTaskStatus.Reviewing] = [AgentTaskStatus.AwaitingReview, AgentTaskStatus.Completed, AgentTaskStatus.ChangesRequested, AgentTaskStatus.Failed, AgentTaskStatus.Cancelled],
        [AgentTaskStatus.AwaitingReview] = [AgentTaskStatus.Completed, AgentTaskStatus.ChangesRequested, AgentTaskStatus.Failed, AgentTaskStatus.Cancelled],
        [AgentTaskStatus.ChangesRequested] = [AgentTaskStatus.InProgress, AgentTaskStatus.Cancelled, AgentTaskStatus.Failed],
        [AgentTaskStatus.Completed] = [],
        [AgentTaskStatus.Failed] = [],
        [AgentTaskStatus.Cancelled] = [],
    };

    public void Transition(AgentTask task, AgentTaskStatus newStatus)
    {
        var current = task.Status;

        if (!ValidTransitions.TryGetValue(current, out var allowed) || !allowed.Contains(newStatus))
            throw new InvalidTaskTransitionException(current, newStatus);

        task.SetStatus(newStatus);
    }

    public bool CanTransition(AgentTask task, AgentTaskStatus newStatus)
    {
        return ValidTransitions.TryGetValue(task.Status, out var allowed) && allowed.Contains(newStatus);
    }

    public void RetryFailedTask(AgentTask task)
    {
        if (task.Status != AgentTaskStatus.Failed)
            throw new InvalidTaskTransitionException(task.Status, AgentTaskStatus.Planning);

        task.SetStatus(AgentTaskStatus.Planning);
    }
}
