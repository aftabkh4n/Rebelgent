using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>
/// EF Core persistence record for <see cref="AgentTask"/>.
/// Kept internal to the persistence adapter — never exposed to callers of IAgentTaskRepository.
///
/// DateTimeOffset values are stored as UTC ticks (long/INTEGER) because SQLite does not
/// support ORDER BY on DateTimeOffset columns when using EF Core.
/// </summary>
internal class AgentTaskRecord
{
    public Guid Id { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AgentRole AssignedRole { get; set; }
    public AgentTaskStatus Status { get; set; }
    public RiskLevel Risk { get; set; }
    public long CreatedAt { get; set; }
    public long? StartedAt { get; set; }
    public long? CompletedAt { get; set; }
    public string? BranchName { get; set; }
    public int? PullRequestNumber { get; set; }
    public string? PullRequestUrl { get; set; }
    public long? PullRequestCreatedAt { get; set; }

    public static AgentTaskRecord FromDomain(AgentTask task) => new()
    {
        Id = task.Id,
        ProjectId = task.ProjectId,
        Title = task.Title,
        Description = task.Description,
        AssignedRole = task.AssignedRole,
        Status = task.Status,
        Risk = task.Risk,
        CreatedAt = task.CreatedAt.UtcTicks,
        StartedAt = task.StartedAt?.UtcTicks,
        CompletedAt = task.CompletedAt?.UtcTicks,
        BranchName = task.BranchName,
        PullRequestNumber = task.PullRequestNumber,
        PullRequestUrl = task.PullRequestUrl,
        PullRequestCreatedAt = task.PullRequestCreatedAt?.UtcTicks
    };

    public AgentTask ToDomain() => AgentTask.Reconstitute(
        Id, ProjectId, Title, Description,
        AssignedRole, Status, Risk,
        new DateTimeOffset(CreatedAt, TimeSpan.Zero),
        StartedAt.HasValue ? new DateTimeOffset(StartedAt.Value, TimeSpan.Zero) : null,
        CompletedAt.HasValue ? new DateTimeOffset(CompletedAt.Value, TimeSpan.Zero) : null,
        BranchName, PullRequestNumber,
        PullRequestUrl,
        PullRequestCreatedAt.HasValue ? new DateTimeOffset(PullRequestCreatedAt.Value, TimeSpan.Zero) : null);
}
