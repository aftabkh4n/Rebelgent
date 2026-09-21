using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="ApprovalRecord"/>.</summary>
internal class ApprovalRecordDbRecord
{
    public Guid Id { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public Guid HumanId { get; set; }
    public string IdentityProvider { get; set; } = string.Empty;
    public string ExternalIdentityId { get; set; } = string.Empty;
    public long ApprovedAt { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public Guid AuditEventId { get; set; }

    public static ApprovalRecordDbRecord FromDomain(ApprovalRecord r) => new()
    {
        Id = r.Id,
        ActionType = r.ActionType,
        ResourceId = r.ResourceId,
        HumanId = r.HumanId,
        IdentityProvider = r.IdentityProvider,
        ExternalIdentityId = r.ExternalIdentityId,
        ApprovedAt = r.ApprovedAt.UtcTicks,
        RequestId = r.RequestId,
        AuditEventId = r.AuditEventId
    };

    public ApprovalRecord ToDomain() => ApprovalRecord.Reconstitute(
        Id, ActionType, ResourceId, HumanId, IdentityProvider, ExternalIdentityId,
        new DateTimeOffset(ApprovedAt, TimeSpan.Zero), RequestId, AuditEventId);
}
