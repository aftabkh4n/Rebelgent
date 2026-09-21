namespace Rebelgent.Core.Domain;

/// <summary>
/// An immutable version snapshot of an agent's prompt template and capabilities.
/// Once created, versions are never updated — superseding creates a new version.
/// </summary>
public sealed class AgentVersion
{
    public Guid Id { get; private set; }
    public Guid AgentDefinitionId { get; private set; }
    public string Version { get; private set; }
    public string PromptTemplate { get; private set; }

    /// <summary>JSON or comma-separated list of capabilities.</summary>
    public string Capabilities { get; private set; }

    public string? ProviderConfigurationReference { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedByProposalId { get; private set; }
    public string? EvaluationSummary { get; private set; }
    public AgentVersionStatus Status { get; private set; }

    public AgentVersion(
        Guid agentDefinitionId,
        string version,
        string promptTemplate,
        string capabilities,
        string? providerConfigurationReference = null,
        Guid? createdByProposalId = null)
    {
        if (agentDefinitionId == Guid.Empty)
            throw new ArgumentException("AgentDefinitionId cannot be empty.", nameof(agentDefinitionId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be empty.", nameof(version));
        if (string.IsNullOrWhiteSpace(promptTemplate))
            throw new ArgumentException("PromptTemplate cannot be empty.", nameof(promptTemplate));
        if (string.IsNullOrWhiteSpace(capabilities))
            throw new ArgumentException("Capabilities cannot be empty.", nameof(capabilities));

        Id = Guid.NewGuid();
        AgentDefinitionId = agentDefinitionId;
        Version = version;
        PromptTemplate = promptTemplate;
        Capabilities = capabilities;
        ProviderConfigurationReference = providerConfigurationReference;
        CreatedByProposalId = createdByProposalId;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = AgentVersionStatus.Draft;
    }

    internal static AgentVersion Reconstitute(
        Guid id, Guid agentDefinitionId, string version, string promptTemplate, string capabilities,
        string? providerConfigurationReference, DateTimeOffset createdAt, Guid? createdByProposalId,
        string? evaluationSummary, AgentVersionStatus status)
    {
        return new AgentVersion
        {
            Id = id,
            AgentDefinitionId = agentDefinitionId,
            Version = version,
            PromptTemplate = promptTemplate,
            Capabilities = capabilities,
            ProviderConfigurationReference = providerConfigurationReference,
            CreatedAt = createdAt,
            CreatedByProposalId = createdByProposalId,
            EvaluationSummary = evaluationSummary,
            Status = status
        };
    }

    private AgentVersion()
    {
        Version = string.Empty;
        PromptTemplate = string.Empty;
        Capabilities = string.Empty;
    }

    public void Activate()
    {
        if (Status == AgentVersionStatus.Superseded)
            throw new InvalidOperationException("Cannot activate a superseded version.");
        Status = AgentVersionStatus.Active;
    }

    public void Supersede()
    {
        Status = AgentVersionStatus.Superseded;
    }
}
