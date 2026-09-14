namespace Rebelgent.Core.Domain;

/// <summary>Records the outcome of one agent coding execution run against a task.</summary>
public class AgentExecutionRecord
{
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public string ProjectId { get; private set; }
    public string WorkspacePath { get; private set; }
    public string BranchName { get; private set; }
    public ExecutionStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? AgentOutput { get; private set; }
    public string? BuildOutput { get; private set; }
    public string? TestOutput { get; private set; }
    public string? ErrorMessage { get; private set; }

    public AgentExecutionRecord(Guid taskId, string projectId, string workspacePath, string branchName)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("ProjectId cannot be empty.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new ArgumentException("WorkspacePath cannot be empty.", nameof(workspacePath));
        if (string.IsNullOrWhiteSpace(branchName))
            throw new ArgumentException("BranchName cannot be empty.", nameof(branchName));

        Id = Guid.NewGuid();
        TaskId = taskId;
        ProjectId = projectId;
        WorkspacePath = workspacePath;
        BranchName = branchName;
        Status = ExecutionStatus.Queued;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRunning()
    {
        Status = ExecutionStatus.Running;
    }

    public void Complete(string agentOutput, string buildOutput, string testOutput, bool buildSucceeded, bool testsSucceeded)
    {
        AgentOutput = agentOutput;
        BuildOutput = buildOutput;
        TestOutput = testOutput;
        Status = buildSucceeded && testsSucceeded ? ExecutionStatus.Succeeded : ExecutionStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string errorMessage)
    {
        ErrorMessage = errorMessage;
        Status = ExecutionStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkTimedOut(string errorMessage)
    {
        ErrorMessage = errorMessage;
        Status = ExecutionStatus.TimedOut;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    internal static AgentExecutionRecord Reconstitute(
        Guid id,
        Guid taskId,
        string projectId,
        string workspacePath,
        string branchName,
        ExecutionStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        string? agentOutput,
        string? buildOutput,
        string? testOutput,
        string? errorMessage)
    {
        return new AgentExecutionRecord
        {
            Id = id,
            TaskId = taskId,
            ProjectId = projectId,
            WorkspacePath = workspacePath,
            BranchName = branchName,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            AgentOutput = agentOutput,
            BuildOutput = buildOutput,
            TestOutput = testOutput,
            ErrorMessage = errorMessage
        };
    }

    private AgentExecutionRecord()
    {
        ProjectId = string.Empty;
        WorkspacePath = string.Empty;
        BranchName = string.Empty;
    }
}
