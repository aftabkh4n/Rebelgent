using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="AgentVersion"/>.</summary>
internal class AgentVersionDbRecord
{
    public Guid Id { get; set; }
    public Guid AgentDefinitionId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = string.Empty;
    public string Capabilities { get; set; } = string.Empty;
    public string? ProviderConfigurationReference { get; set; }
    public long CreatedAt { get; set; }
    public Guid? CreatedByProposalId { get; set; }
    public string? EvaluationSummary { get; set; }
    public int Status { get; set; }

    public static AgentVersionDbRecord FromDomain(AgentVersion v) => new()
    {
        Id = v.Id,
        AgentDefinitionId = v.AgentDefinitionId,
        Version = v.Version,
        PromptTemplate = v.PromptTemplate,
        Capabilities = v.Capabilities,
        ProviderConfigurationReference = v.ProviderConfigurationReference,
        CreatedAt = v.CreatedAt.UtcTicks,
        CreatedByProposalId = v.CreatedByProposalId,
        EvaluationSummary = v.EvaluationSummary,
        Status = (int)v.Status
    };

    public AgentVersion ToDomain() => AgentVersion.Reconstitute(
        Id, AgentDefinitionId, Version, PromptTemplate, Capabilities,
        ProviderConfigurationReference, new DateTimeOffset(CreatedAt, TimeSpan.Zero),
        CreatedByProposalId, EvaluationSummary, (AgentVersionStatus)Status);
}
