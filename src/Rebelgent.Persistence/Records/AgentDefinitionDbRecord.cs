using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="AgentDefinition"/>.</summary>
internal class AgentDefinitionDbRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Role { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Status { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public long CreatedAt { get; set; }
    public Guid CreatedByHumanId { get; set; }
    public long? ActivatedAt { get; set; }
    public long? SuspendedAt { get; set; }
    public long? RetiredAt { get; set; }

    public static AgentDefinitionDbRecord FromDomain(AgentDefinition d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Role = (int)d.Role,
        Purpose = d.Purpose,
        Description = d.Description,
        Status = (int)d.Status,
        CurrentVersionId = d.CurrentVersionId,
        CreatedAt = d.CreatedAt.UtcTicks,
        CreatedByHumanId = d.CreatedByHumanId,
        ActivatedAt = d.ActivatedAt?.UtcTicks,
        SuspendedAt = d.SuspendedAt?.UtcTicks,
        RetiredAt = d.RetiredAt?.UtcTicks
    };

    public AgentDefinition ToDomain() => AgentDefinition.Reconstitute(
        Id, Name, (AgentRole)Role, Purpose, Description, (AgentLifecycleStatus)Status,
        CurrentVersionId, new DateTimeOffset(CreatedAt, TimeSpan.Zero), CreatedByHumanId,
        ActivatedAt.HasValue ? new DateTimeOffset(ActivatedAt.Value, TimeSpan.Zero) : null,
        SuspendedAt.HasValue ? new DateTimeOffset(SuspendedAt.Value, TimeSpan.Zero) : null,
        RetiredAt.HasValue ? new DateTimeOffset(RetiredAt.Value, TimeSpan.Zero) : null);
}
