using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>
/// EF Core persistence record for <see cref="ApprovalRequest"/>.
/// Kept internal to the persistence adapter.
/// </summary>
internal class ApprovalRequestRecord
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public ApprovalType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public ApprovalDecision? Decision { get; set; }
}
