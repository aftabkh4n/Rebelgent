using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="AgentEvolutionProposal"/>.</summary>
internal class AgentEvolutionProposalDbRecord
{
    public Guid Id { get; set; }
    public int ProposalType { get; set; }
    public string TargetProjectId { get; set; } = string.Empty;
    public Guid? TargetAgentId { get; set; }
    public string? ProposedAgentName { get; set; }
    public int? ProposedRole { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string SuggestedChange { get; set; } = string.Empty;
    public string? SuggestedPrompt { get; set; }
    public string? SuggestedCapabilities { get; set; }
    public int RiskLevel { get; set; }
    public string? EvaluationSummary { get; set; }
    public int Status { get; set; }
    public long CreatedAt { get; set; }
    public long? ApprovedAt { get; set; }
    public Guid? CreatedTaskId { get; set; }
    public string? ImplementationMergeCommitSha { get; set; }
    public int? ImplementationPullRequestNumber { get; set; }
    public long? ImplementedAt { get; set; }

    public static AgentEvolutionProposalDbRecord FromDomain(AgentEvolutionProposal p) => new()
    {
        Id = p.Id,
        ProposalType = (int)p.ProposalType,
        TargetProjectId = p.TargetProjectId,
        TargetAgentId = p.TargetAgentId,
        ProposedAgentName = p.ProposedAgentName,
        ProposedRole = p.ProposedRole.HasValue ? (int)p.ProposedRole.Value : null,
        Purpose = p.Purpose,
        Evidence = p.Evidence,
        SuggestedChange = p.SuggestedChange,
        SuggestedPrompt = p.SuggestedPrompt,
        SuggestedCapabilities = p.SuggestedCapabilities,
        RiskLevel = (int)p.RiskLevel,
        EvaluationSummary = p.EvaluationSummary,
        Status = (int)p.Status,
        CreatedAt = p.CreatedAt.UtcTicks,
        ApprovedAt = p.ApprovedAt?.UtcTicks,
        CreatedTaskId = p.CreatedTaskId,
        ImplementationMergeCommitSha = p.ImplementationMergeCommitSha,
        ImplementationPullRequestNumber = p.ImplementationPullRequestNumber,
        ImplementedAt = p.ImplementedAt?.UtcTicks
    };

    public AgentEvolutionProposal ToDomain() => AgentEvolutionProposal.Reconstitute(
        Id, (AgentEvolutionProposalType)ProposalType, TargetProjectId, TargetAgentId, ProposedAgentName,
        ProposedRole.HasValue ? (AgentRole?)ProposedRole.Value : null,
        Purpose, Evidence, SuggestedChange, SuggestedPrompt, SuggestedCapabilities,
        (RiskLevel)RiskLevel, EvaluationSummary, (AgentEvolutionProposalStatus)Status,
        new DateTimeOffset(CreatedAt, TimeSpan.Zero),
        ApprovedAt.HasValue ? new DateTimeOffset(ApprovedAt.Value, TimeSpan.Zero) : null,
        CreatedTaskId,
        ImplementationMergeCommitSha,
        ImplementationPullRequestNumber,
        ImplementedAt.HasValue ? new DateTimeOffset(ImplementedAt.Value, TimeSpan.Zero) : null);
}
