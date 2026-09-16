namespace Rebelgent.Core.Domain;

/// <summary>
/// A proposed improvement to Rebelgent's own agent instructions or workflow, derived from
/// deterministic analysis of historical task execution failures. Never applied automatically —
/// approval only creates a normal <see cref="AgentTask"/> that goes through the standard
/// Developer → build/test → QA → Reviewer → human approval → PR → merge pipeline.
/// </summary>
public class ImprovementProposal
{
    public Guid Id { get; private set; }

    /// <summary>The registered project ID this proposal's resulting task must be created against.
    /// Always validated against <c>IProjectRegistry</c> before a task is created — never an
    /// arbitrary filesystem path.</summary>
    public string TargetProjectId { get; private set; }

    public string Title { get; private set; }
    public string Description { get; private set; }

    /// <summary>Human-readable summary of the historical evidence this proposal is based on.</summary>
    public string Evidence { get; private set; }

    /// <summary>The area of Rebelgent this proposal targets, e.g. "Developer Prompt", "QA Checklist".</summary>
    public string TargetArea { get; private set; }

    public string SuggestedChange { get; private set; }
    public RiskLevel RiskLevel { get; private set; }
    public ImprovementProposalStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? EvaluationSummary { get; private set; }

    /// <summary>Deterministic fingerprint of the (category, target area, evidence) this proposal
    /// was raised from — used to prevent duplicate proposals for the same unresolved issue.</summary>
    public string EvidenceFingerprint { get; private set; }

    /// <summary>Set once a human approves this proposal and a normal <see cref="AgentTask"/> is created for it.</summary>
    public Guid? CreatedTaskId { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public ImprovementProposal(
        string targetProjectId,
        string title,
        string description,
        string evidence,
        string targetArea,
        string suggestedChange,
        RiskLevel riskLevel,
        string evidenceFingerprint)
    {
        if (string.IsNullOrWhiteSpace(targetProjectId))
            throw new ArgumentException("Target project ID cannot be empty.", nameof(targetProjectId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty.", nameof(description));
        if (string.IsNullOrWhiteSpace(evidence))
            throw new ArgumentException("Evidence cannot be empty.", nameof(evidence));
        if (string.IsNullOrWhiteSpace(targetArea))
            throw new ArgumentException("Target area cannot be empty.", nameof(targetArea));
        if (string.IsNullOrWhiteSpace(suggestedChange))
            throw new ArgumentException("Suggested change cannot be empty.", nameof(suggestedChange));
        if (string.IsNullOrWhiteSpace(evidenceFingerprint))
            throw new ArgumentException("Evidence fingerprint cannot be empty.", nameof(evidenceFingerprint));

        Id = Guid.NewGuid();
        TargetProjectId = targetProjectId;
        Title = title;
        Description = description;
        Evidence = evidence;
        TargetArea = targetArea;
        SuggestedChange = suggestedChange;
        RiskLevel = riskLevel;
        EvidenceFingerprint = evidenceFingerprint;
        Status = ImprovementProposalStatus.Proposed;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    internal static ImprovementProposal Reconstitute(
        Guid id, string targetProjectId, string title, string description, string evidence, string targetArea,
        string suggestedChange, RiskLevel riskLevel, ImprovementProposalStatus status,
        DateTimeOffset createdAt, string? evaluationSummary, string evidenceFingerprint,
        Guid? createdTaskId, DateTimeOffset? decidedAt)
    {
        return new ImprovementProposal
        {
            Id = id,
            TargetProjectId = targetProjectId,
            Title = title,
            Description = description,
            Evidence = evidence,
            TargetArea = targetArea,
            SuggestedChange = suggestedChange,
            RiskLevel = riskLevel,
            Status = status,
            CreatedAt = createdAt,
            EvaluationSummary = evaluationSummary,
            EvidenceFingerprint = evidenceFingerprint,
            CreatedTaskId = createdTaskId,
            DecidedAt = decidedAt
        };
    }

    private ImprovementProposal()
    {
        TargetProjectId = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        Evidence = string.Empty;
        TargetArea = string.Empty;
        SuggestedChange = string.Empty;
        EvidenceFingerprint = string.Empty;
    }

    public void BeginEvaluation()
    {
        if (Status != ImprovementProposalStatus.Proposed)
            throw new InvalidOperationException($"Cannot begin evaluation from status '{Status}'.");
        Status = ImprovementProposalStatus.Evaluating;
    }

    public void CompleteEvaluation(string evaluationSummary)
    {
        if (Status != ImprovementProposalStatus.Evaluating)
            throw new InvalidOperationException($"Cannot complete evaluation from status '{Status}'.");
        if (string.IsNullOrWhiteSpace(evaluationSummary))
            throw new ArgumentException("Evaluation summary cannot be empty.", nameof(evaluationSummary));

        EvaluationSummary = evaluationSummary;
        Status = ImprovementProposalStatus.AwaitingApproval;
    }

    /// <summary>Human approval: records the normal <see cref="AgentTask"/> created for this
    /// proposal. Never runs the task automatically.</summary>
    public void Approve(Guid createdTaskId)
    {
        if (Status != ImprovementProposalStatus.AwaitingApproval)
            throw new InvalidOperationException($"Cannot approve a proposal in status '{Status}'.");
        if (createdTaskId == Guid.Empty)
            throw new ArgumentException("Created task ID cannot be empty.", nameof(createdTaskId));

        CreatedTaskId = createdTaskId;
        Status = ImprovementProposalStatus.Approved;
        DecidedAt = DateTimeOffset.UtcNow;
    }

    public void Reject()
    {
        if (Status is not (ImprovementProposalStatus.Proposed or ImprovementProposalStatus.Evaluating or ImprovementProposalStatus.AwaitingApproval))
            throw new InvalidOperationException($"Cannot reject a proposal in status '{Status}'.");

        Status = ImprovementProposalStatus.Rejected;
        DecidedAt = DateTimeOffset.UtcNow;
    }

    public void MarkImplemented()
    {
        if (Status != ImprovementProposalStatus.Approved)
            throw new InvalidOperationException($"Cannot mark implemented from status '{Status}'.");
        Status = ImprovementProposalStatus.Implemented;
    }

    public void MarkFailed()
    {
        Status = ImprovementProposalStatus.Failed;
    }
}
