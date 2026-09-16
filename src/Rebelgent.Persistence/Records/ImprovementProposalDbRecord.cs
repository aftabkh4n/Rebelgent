using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="ImprovementProposal"/>.</summary>
internal class ImprovementProposalDbRecord
{
    public Guid Id { get; set; }
    public string TargetProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string TargetArea { get; set; } = string.Empty;
    public string SuggestedChange { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; }
    public ImprovementProposalStatus Status { get; set; }
    public long CreatedAt { get; set; }
    public string? EvaluationSummary { get; set; }
    public string EvidenceFingerprint { get; set; } = string.Empty;
    public Guid? CreatedTaskId { get; set; }
    public long? DecidedAt { get; set; }

    public static ImprovementProposalDbRecord FromDomain(ImprovementProposal proposal) => new()
    {
        Id = proposal.Id,
        TargetProjectId = proposal.TargetProjectId,
        Title = proposal.Title,
        Description = proposal.Description,
        Evidence = proposal.Evidence,
        TargetArea = proposal.TargetArea,
        SuggestedChange = proposal.SuggestedChange,
        RiskLevel = proposal.RiskLevel,
        Status = proposal.Status,
        CreatedAt = proposal.CreatedAt.UtcTicks,
        EvaluationSummary = proposal.EvaluationSummary,
        EvidenceFingerprint = proposal.EvidenceFingerprint,
        CreatedTaskId = proposal.CreatedTaskId,
        DecidedAt = proposal.DecidedAt?.UtcTicks
    };

    public ImprovementProposal ToDomain() => ImprovementProposal.Reconstitute(
        Id, TargetProjectId, Title, Description, Evidence, TargetArea, SuggestedChange, RiskLevel, Status,
        new DateTimeOffset(CreatedAt, TimeSpan.Zero), EvaluationSummary, EvidenceFingerprint,
        CreatedTaskId, DecidedAt.HasValue ? new DateTimeOffset(DecidedAt.Value, TimeSpan.Zero) : null);
}
