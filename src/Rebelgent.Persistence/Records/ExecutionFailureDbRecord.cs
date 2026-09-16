using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="ExecutionFailure"/>.</summary>
internal class ExecutionFailureDbRecord
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid? ExecutionId { get; set; }
    public FailureCategory Category { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public long DetectedAt { get; set; }

    public static ExecutionFailureDbRecord FromDomain(ExecutionFailure failure) => new()
    {
        Id = failure.Id,
        TaskId = failure.TaskId,
        ExecutionId = failure.ExecutionId,
        Category = failure.Category,
        Source = failure.Source,
        Message = failure.Message,
        DetectedAt = failure.DetectedAt.UtcTicks
    };

    public ExecutionFailure ToDomain() => ExecutionFailure.Reconstitute(
        Id, TaskId, ExecutionId, Category, Source, Message,
        new DateTimeOffset(DetectedAt, TimeSpan.Zero));
}
