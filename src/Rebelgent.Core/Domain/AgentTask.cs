namespace Rebelgent.Core.Domain;

/// <summary>Represents a unit of work assigned to an agent within a project.</summary>
public class AgentTask
{
    public Guid Id { get; private set; }
    public string ProjectId { get; private set; }
    public string Title { get; private set; }
    public string Description { get; private set; }
    public AgentRole AssignedRole { get; private set; }
    public AgentTaskStatus Status { get; private set; }
    public RiskLevel Risk { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? BranchName { get; private set; }
    public int? PullRequestNumber { get; private set; }
    public string? PullRequestUrl { get; private set; }
    public DateTimeOffset? PullRequestCreatedAt { get; private set; }
    public DateTimeOffset? MergedAt { get; private set; }
    public string? MergeCommitSha { get; private set; }
    public string? MergeMethod { get; private set; }

    public AgentTask(
        string projectId,
        string title,
        string description,
        AgentRole assignedRole,
        RiskLevel risk = RiskLevel.Low)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project ID cannot be empty.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));
        if (description is null)
            throw new ArgumentNullException(nameof(description));

        Id = Guid.NewGuid();
        ProjectId = projectId;
        Title = title;
        Description = description;
        AssignedRole = assignedRole;
        Risk = risk;
        Status = AgentTaskStatus.Created;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Reconstitutes an AgentTask from persisted data.
    /// For use by the persistence layer only.
    /// </summary>
    internal static AgentTask Reconstitute(
        Guid id,
        string projectId,
        string title,
        string description,
        AgentRole assignedRole,
        AgentTaskStatus status,
        RiskLevel risk,
        DateTimeOffset createdAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? branchName,
        int? pullRequestNumber,
        string? pullRequestUrl = null,
        DateTimeOffset? pullRequestCreatedAt = null,
        DateTimeOffset? mergedAt = null,
        string? mergeCommitSha = null,
        string? mergeMethod = null)
    {
        return new AgentTask
        {
            Id = id,
            ProjectId = projectId,
            Title = title,
            Description = description,
            AssignedRole = assignedRole,
            Status = status,
            Risk = risk,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            BranchName = branchName,
            PullRequestNumber = pullRequestNumber,
            PullRequestUrl = pullRequestUrl,
            PullRequestCreatedAt = pullRequestCreatedAt,
            MergedAt = mergedAt,
            MergeCommitSha = mergeCommitSha,
            MergeMethod = mergeMethod
        };
    }

    // Private parameterless constructor for reconstitution only.
    private AgentTask()
    {
        ProjectId = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
    }

    internal void SetStatus(AgentTaskStatus newStatus)
    {
        Status = newStatus;

        if (newStatus == AgentTaskStatus.InProgress && StartedAt is null)
            StartedAt = DateTimeOffset.UtcNow;

        if (newStatus is AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled)
            CompletedAt = DateTimeOffset.UtcNow;
    }

    public void SetBranchName(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            throw new ArgumentException("Branch name cannot be empty.", nameof(branchName));
        BranchName = branchName;
    }

    public void SetPullRequestNumber(int number)
    {
        if (number <= 0)
            throw new ArgumentOutOfRangeException(nameof(number), "Pull request number must be positive.");
        PullRequestNumber = number;
    }

    public void SetPullRequestInfo(int number, string url)
    {
        if (number <= 0)
            throw new ArgumentOutOfRangeException(nameof(number), "Pull request number must be positive.");
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Pull request URL cannot be empty.", nameof(url));
        PullRequestNumber = number;
        PullRequestUrl = url;
        PullRequestCreatedAt = DateTimeOffset.UtcNow;
    }

    public void SetMergeInfo(string mergeCommitSha, string mergeMethod)
    {
        if (string.IsNullOrWhiteSpace(mergeCommitSha))
            throw new ArgumentException("Merge commit SHA cannot be empty.", nameof(mergeCommitSha));
        if (string.IsNullOrWhiteSpace(mergeMethod))
            throw new ArgumentException("Merge method cannot be empty.", nameof(mergeMethod));
        MergeCommitSha = mergeCommitSha;
        MergeMethod = mergeMethod;
        MergedAt = DateTimeOffset.UtcNow;
    }
}
