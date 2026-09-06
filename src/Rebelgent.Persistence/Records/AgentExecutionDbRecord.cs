using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>
/// EF Core record for <see cref="AgentExecutionRecord"/>.
/// DateTimeOffset values stored as UTC ticks (long/INTEGER) to avoid SQLite ORDER BY issues.
/// </summary>
internal class AgentExecutionDbRecord
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public int Role { get; set; }
    public string Provider { get; set; } = "ClaudeCode";
    public ExecutionStatus Status { get; set; }
    public long StartedAt { get; set; }
    public long? CompletedAt { get; set; }
    public string? AgentOutput { get; set; }
    public string? BuildOutput { get; set; }
    public string? TestOutput { get; set; }
    public string? Findings { get; set; }
    public string? ErrorMessage { get; set; }
    public bool? BuildSucceeded { get; set; }
    public bool? TestsSucceeded { get; set; }
    public string? CommitSha { get; set; }

    public static AgentExecutionDbRecord FromDomain(AgentExecutionRecord record) => new()
    {
        Id = record.Id,
        TaskId = record.TaskId,
        ProjectId = record.ProjectId,
        WorkspacePath = record.WorkspacePath,
        BranchName = record.BranchName,
        Role = (int)record.Role,
        Provider = record.Provider,
        Status = record.Status,
        StartedAt = record.StartedAt.UtcTicks,
        CompletedAt = record.CompletedAt?.UtcTicks,
        AgentOutput = record.AgentOutput,
        BuildOutput = record.BuildOutput,
        TestOutput = record.TestOutput,
        Findings = record.Findings,
        ErrorMessage = record.ErrorMessage,
        BuildSucceeded = record.BuildSucceeded,
        TestsSucceeded = record.TestsSucceeded,
        CommitSha = record.CommitSha
    };

    public AgentExecutionRecord ToDomain() => AgentExecutionRecord.Reconstitute(
        Id, TaskId, ProjectId, WorkspacePath, BranchName, Status,
        new DateTimeOffset(StartedAt, TimeSpan.Zero),
        CompletedAt.HasValue ? new DateTimeOffset(CompletedAt.Value, TimeSpan.Zero) : null,
        AgentOutput, BuildOutput, TestOutput, ErrorMessage,
        (AgentRole)Role, Provider, Findings, BuildSucceeded, TestsSucceeded, CommitSha);
}
