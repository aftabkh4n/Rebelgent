namespace Rebelgent.Core.Domain;

/// <summary>A single categorized failure detected from historical task execution, used as
/// evidence for self-improvement analysis. Immutable once recorded.</summary>
public class ExecutionFailure
{
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }

    /// <summary>Optional link to the originating <see cref="AgentExecutionRecord"/>. Null for
    /// failures derived from Release/Package records rather than an agent execution.</summary>
    public Guid? ExecutionId { get; private set; }

    public FailureCategory Category { get; private set; }

    /// <summary>Human-readable origin of the failure, e.g. "BackendDeveloper", "QaEngineer", "ReleaseManager", "PackagePublisher".</summary>
    public string Source { get; private set; }

    public string Message { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; }

    public ExecutionFailure(Guid taskId, Guid? executionId, FailureCategory category, string source, string message)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source cannot be empty.", nameof(source));
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        Id = Guid.NewGuid();
        TaskId = taskId;
        ExecutionId = executionId;
        Category = category;
        Source = source;
        Message = message;
        DetectedAt = DateTimeOffset.UtcNow;
    }

    internal static ExecutionFailure Reconstitute(
        Guid id, Guid taskId, Guid? executionId, FailureCategory category,
        string source, string message, DateTimeOffset detectedAt)
    {
        return new ExecutionFailure
        {
            Id = id,
            TaskId = taskId,
            ExecutionId = executionId,
            Category = category,
            Source = source,
            Message = message,
            DetectedAt = detectedAt
        };
    }

    private ExecutionFailure()
    {
        Source = string.Empty;
        Message = string.Empty;
    }
}
