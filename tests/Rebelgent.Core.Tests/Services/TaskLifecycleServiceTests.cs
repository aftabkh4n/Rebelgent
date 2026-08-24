using Rebelgent.Core.Domain;
using Rebelgent.Core.Exceptions;
using Rebelgent.Core.Services;

namespace Rebelgent.Core.Tests.Services;

public class TaskLifecycleServiceTests
{
    private readonly TaskLifecycleService _lifecycle = new();

    private AgentTask CreateTask() =>
        new("project-1", "Test task", "Description", AgentRole.BackendDeveloper);

    // --- Happy path: the normal flow ---

    [Fact]
    public void Created_To_Planning_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        Assert.Equal(AgentTaskStatus.Planning, task.Status);
    }

    [Fact]
    public void Planning_To_AwaitingApproval_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        Assert.Equal(AgentTaskStatus.AwaitingApproval, task.Status);
    }

    [Fact]
    public void AwaitingApproval_To_Approved_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        _lifecycle.Transition(task, AgentTaskStatus.Approved);
        Assert.Equal(AgentTaskStatus.Approved, task.Status);
    }

    [Fact]
    public void Approved_To_InProgress_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        _lifecycle.Transition(task, AgentTaskStatus.Approved);
        _lifecycle.Transition(task, AgentTaskStatus.InProgress);
        Assert.Equal(AgentTaskStatus.InProgress, task.Status);
        Assert.NotNull(task.StartedAt);
    }

    [Fact]
    public void InProgress_To_Testing_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        _lifecycle.Transition(task, AgentTaskStatus.Approved);
        _lifecycle.Transition(task, AgentTaskStatus.InProgress);
        _lifecycle.Transition(task, AgentTaskStatus.Testing);
        Assert.Equal(AgentTaskStatus.Testing, task.Status);
    }

    [Fact]
    public void Testing_To_Reviewing_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        _lifecycle.Transition(task, AgentTaskStatus.Approved);
        _lifecycle.Transition(task, AgentTaskStatus.InProgress);
        _lifecycle.Transition(task, AgentTaskStatus.Testing);
        _lifecycle.Transition(task, AgentTaskStatus.Reviewing);
        Assert.Equal(AgentTaskStatus.Reviewing, task.Status);
    }

    [Fact]
    public void Reviewing_To_Completed_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        _lifecycle.Transition(task, AgentTaskStatus.Approved);
        _lifecycle.Transition(task, AgentTaskStatus.InProgress);
        _lifecycle.Transition(task, AgentTaskStatus.Testing);
        _lifecycle.Transition(task, AgentTaskStatus.Reviewing);
        _lifecycle.Transition(task, AgentTaskStatus.Completed);
        Assert.Equal(AgentTaskStatus.Completed, task.Status);
        Assert.NotNull(task.CompletedAt);
    }

    // --- ChangesRequested paths ---

    [Fact]
    public void Reviewing_To_ChangesRequested_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        _lifecycle.Transition(task, AgentTaskStatus.Approved);
        _lifecycle.Transition(task, AgentTaskStatus.InProgress);
        _lifecycle.Transition(task, AgentTaskStatus.Testing);
        _lifecycle.Transition(task, AgentTaskStatus.Reviewing);
        _lifecycle.Transition(task, AgentTaskStatus.ChangesRequested);
        Assert.Equal(AgentTaskStatus.ChangesRequested, task.Status);
    }

    [Fact]
    public void ChangesRequested_To_InProgress_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        _lifecycle.Transition(task, AgentTaskStatus.Approved);
        _lifecycle.Transition(task, AgentTaskStatus.InProgress);
        _lifecycle.Transition(task, AgentTaskStatus.Testing);
        _lifecycle.Transition(task, AgentTaskStatus.Reviewing);
        _lifecycle.Transition(task, AgentTaskStatus.ChangesRequested);
        _lifecycle.Transition(task, AgentTaskStatus.InProgress);
        Assert.Equal(AgentTaskStatus.InProgress, task.Status);
    }

    [Fact]
    public void Testing_To_ChangesRequested_Succeeds()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        _lifecycle.Transition(task, AgentTaskStatus.Approved);
        _lifecycle.Transition(task, AgentTaskStatus.InProgress);
        _lifecycle.Transition(task, AgentTaskStatus.Testing);
        _lifecycle.Transition(task, AgentTaskStatus.ChangesRequested);
        Assert.Equal(AgentTaskStatus.ChangesRequested, task.Status);
    }

    // --- Terminal states ---

    [Fact]
    public void CompletedTask_CannotTransitionToAnyStatus()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Planning);
        _lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        _lifecycle.Transition(task, AgentTaskStatus.Approved);
        _lifecycle.Transition(task, AgentTaskStatus.InProgress);
        _lifecycle.Transition(task, AgentTaskStatus.Testing);
        _lifecycle.Transition(task, AgentTaskStatus.Reviewing);
        _lifecycle.Transition(task, AgentTaskStatus.Completed);

        var ex = Assert.Throws<InvalidTaskTransitionException>(() =>
            _lifecycle.Transition(task, AgentTaskStatus.InProgress));
        Assert.Equal(AgentTaskStatus.Completed, ex.FromStatus);
    }

    [Fact]
    public void CancelledTask_CannotRestart()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Cancelled);

        var ex = Assert.Throws<InvalidTaskTransitionException>(() =>
            _lifecycle.Transition(task, AgentTaskStatus.Planning));
        Assert.Equal(AgentTaskStatus.Cancelled, ex.FromStatus);
    }

    [Fact]
    public void FailedTask_CannotRestart()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Failed);

        var ex = Assert.Throws<InvalidTaskTransitionException>(() =>
            _lifecycle.Transition(task, AgentTaskStatus.Planning));
        Assert.Equal(AgentTaskStatus.Failed, ex.FromStatus);
    }

    [Fact]
    public void FailedTask_SetsCompletedAt()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Failed);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public void CancelledTask_SetsCompletedAt()
    {
        var task = CreateTask();
        _lifecycle.Transition(task, AgentTaskStatus.Cancelled);
        Assert.NotNull(task.CompletedAt);
    }

    // --- Invalid transitions ---

    [Fact]
    public void InvalidTransition_Throws_WithCorrectStatuses()
    {
        var task = CreateTask();
        var ex = Assert.Throws<InvalidTaskTransitionException>(() =>
            _lifecycle.Transition(task, AgentTaskStatus.Completed));
        Assert.Equal(AgentTaskStatus.Created, ex.FromStatus);
        Assert.Equal(AgentTaskStatus.Completed, ex.ToStatus);
    }

    [Fact]
    public void CanTransition_ReturnsTrueForValidTransition()
    {
        var task = CreateTask();
        Assert.True(_lifecycle.CanTransition(task, AgentTaskStatus.Planning));
    }

    [Fact]
    public void CanTransition_ReturnsFalseForInvalidTransition()
    {
        var task = CreateTask();
        Assert.False(_lifecycle.CanTransition(task, AgentTaskStatus.Completed));
    }
}
