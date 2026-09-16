namespace Rebelgent.Core.Domain;

/// <summary>A point-in-time snapshot of Rebelgent's own operational metrics, computed on demand
/// from persisted task/execution/release/package/failure history. Not itself persisted — always
/// freshly derived so it can never go stale.</summary>
public sealed class AgentMetricsSnapshot
{
    public int TotalTasks { get; }
    public double TaskSuccessRate { get; }
    public double DeveloperFailureRate { get; }
    public double QaPassRate { get; }
    public double ReviewApprovalRate { get; }
    public double AverageRetriesPerTask { get; }
    public double ReleaseFailureRate { get; }
    public double PackageFailureRate { get; }
    public IReadOnlyDictionary<FailureCategory, int> FailuresByCategory { get; }
    public DateTimeOffset GeneratedAt { get; }

    public AgentMetricsSnapshot(
        int totalTasks,
        double taskSuccessRate,
        double developerFailureRate,
        double qaPassRate,
        double reviewApprovalRate,
        double averageRetriesPerTask,
        double releaseFailureRate,
        double packageFailureRate,
        IReadOnlyDictionary<FailureCategory, int> failuresByCategory)
    {
        TotalTasks = totalTasks;
        TaskSuccessRate = taskSuccessRate;
        DeveloperFailureRate = developerFailureRate;
        QaPassRate = qaPassRate;
        ReviewApprovalRate = reviewApprovalRate;
        AverageRetriesPerTask = averageRetriesPerTask;
        ReleaseFailureRate = releaseFailureRate;
        PackageFailureRate = packageFailureRate;
        FailuresByCategory = failuresByCategory ?? throw new ArgumentNullException(nameof(failuresByCategory));
        GeneratedAt = DateTimeOffset.UtcNow;
    }
}
