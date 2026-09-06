namespace Rebelgent.Core.Domain;

/// <summary>Records the outcome of one agent execution run against a task.</summary>
public class AgentExecutionRecord
{
    private const int MaxOutputLength = 10_000;
    private const int MaxFindingsLength = 4_000;

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public string ProjectId { get; private set; }
    public string WorkspacePath { get; private set; }
    public string BranchName { get; private set; }
    public AgentRole Role { get; private set; }
    public string Provider { get; private set; }
    public ExecutionStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? AgentOutput { get; private set; }
    public string? BuildOutput { get; private set; }
    public string? TestOutput { get; private set; }
    public string? Findings { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool? BuildSucceeded { get; private set; }
    public bool? TestsSucceeded { get; private set; }
    public string? CommitSha { get; private set; }

    public AgentExecutionRecord(
        Guid taskId,
        string projectId,
        string workspacePath,
        string branchName,
        AgentRole role = AgentRole.BackendDeveloper,
        string provider = "ClaudeCode")
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
        Role = role;
        Provider = provider;
        Status = ExecutionStatus.Queued;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRunning()
    {
        Status = ExecutionStatus.Running;
    }

    public void Complete(string agentOutput, string buildOutput, string testOutput, bool buildSucceeded, bool testsSucceeded)
    {
        AgentOutput = Truncate(agentOutput, MaxOutputLength);
        BuildOutput = buildOutput;
        TestOutput = testOutput;
        BuildSucceeded = buildSucceeded;
        TestsSucceeded = testsSucceeded;
        Status = buildSucceeded && testsSucceeded ? ExecutionStatus.Succeeded : ExecutionStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void CompleteWithFindings(
        string? agentOutput,
        string? findings,
        bool? buildSucceeded = null,
        bool? testsSucceeded = null)
    {
        AgentOutput = agentOutput is not null ? Truncate(agentOutput, MaxOutputLength) : null;
        Findings = findings is not null ? Truncate(findings, MaxFindingsLength) : null;
        BuildSucceeded = buildSucceeded;
        TestsSucceeded = testsSucceeded;
        Status = ExecutionStatus.Succeeded;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string errorMessage)
    {
        ErrorMessage = errorMessage;
        Status = ExecutionStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void SetCommitSha(string sha)
    {
        CommitSha = sha;
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
        string? errorMessage,
        AgentRole role = AgentRole.BackendDeveloper,
        string provider = "ClaudeCode",
        string? findings = null,
        bool? buildSucceeded = null,
        bool? testsSucceeded = null,
        string? commitSha = null)
    {
        return new AgentExecutionRecord
        {
            Id = id,
            TaskId = taskId,
            ProjectId = projectId,
            WorkspacePath = workspacePath,
            BranchName = branchName,
            Role = role,
            Provider = provider,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            AgentOutput = agentOutput,
            BuildOutput = buildOutput,
            TestOutput = testOutput,
            Findings = findings,
            ErrorMessage = errorMessage,
            BuildSucceeded = buildSucceeded,
            TestsSucceeded = testsSucceeded,
            CommitSha = commitSha
        };
    }

    private AgentExecutionRecord()
    {
        ProjectId = string.Empty;
        WorkspacePath = string.Empty;
        BranchName = string.Empty;
        Provider = string.Empty;
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "\n[truncated]";
}
