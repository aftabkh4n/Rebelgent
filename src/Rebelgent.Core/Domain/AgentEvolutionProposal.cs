namespace Rebelgent.Core.Domain;

/// <summary>
/// A proposal to evolve the agent ecosystem — create a new agent, modify an existing one,
/// or change an agent's lifecycle status. All evolution actions require human approval.
/// </summary>
public sealed class AgentEvolutionProposal
{
    public Guid Id { get; private set; }
    public AgentEvolutionProposalType ProposalType { get; private set; }
    public Guid? TargetAgentId { get; private set; }
    public string? ProposedAgentName { get; private set; }
    public AgentRole? ProposedRole { get; private set; }
    public string Purpose { get; private set; }
    public string Evidence { get; private set; }
    public string SuggestedChange { get; private set; }
    public string? SuggestedPrompt { get; private set; }
    public string? SuggestedCapabilities { get; private set; }
    public RiskLevel RiskLevel { get; private set; }
    public string? EvaluationSummary { get; private set; }
    public AgentEvolutionProposalStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? CreatedTaskId { get; private set; }

    /// <summary>The registered project ID this proposal's implementation task must be created
    /// against. Always validated through <c>IProjectRegistry</c> at analyze-time and again at
    /// approve-time. Never a raw filesystem path.</summary>
    public string TargetProjectId { get; private set; }

    public AgentEvolutionProposal(
        AgentEvolutionProposalType proposalType,
        string targetProjectId,
        string purpose,
        string evidence,
        string suggestedChange,
        RiskLevel riskLevel,
        Guid? targetAgentId = null,
        string? proposedAgentName = null,
        AgentRole? proposedRole = null,
        string? suggestedPrompt = null,
        string? suggestedCapabilities = null)
    {
        if (string.IsNullOrWhiteSpace(targetProjectId))
            throw new ArgumentException("TargetProjectId cannot be empty.", nameof(targetProjectId));
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Purpose cannot be empty.", nameof(purpose));
        if (string.IsNullOrWhiteSpace(evidence))
            throw new ArgumentException("Evidence cannot be empty.", nameof(evidence));
        if (string.IsNullOrWhiteSpace(suggestedChange))
            throw new ArgumentException("SuggestedChange cannot be empty.", nameof(suggestedChange));

        Id = Guid.NewGuid();
        ProposalType = proposalType;
        TargetProjectId = targetProjectId;
        TargetAgentId = targetAgentId;
        ProposedAgentName = proposedAgentName;
        ProposedRole = proposedRole;
        Purpose = purpose;
        Evidence = evidence;
        SuggestedChange = suggestedChange;
        SuggestedPrompt = suggestedPrompt;
        SuggestedCapabilities = suggestedCapabilities;
        RiskLevel = riskLevel;
        Status = AgentEvolutionProposalStatus.Proposed;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    internal static AgentEvolutionProposal Reconstitute(
        Guid id, AgentEvolutionProposalType proposalType, string targetProjectId, Guid? targetAgentId, string? proposedAgentName,
        AgentRole? proposedRole, string purpose, string evidence, string suggestedChange,
        string? suggestedPrompt, string? suggestedCapabilities, RiskLevel riskLevel,
        string? evaluationSummary, AgentEvolutionProposalStatus status, DateTimeOffset createdAt,
        DateTimeOffset? approvedAt, Guid? createdTaskId)
    {
        return new AgentEvolutionProposal
        {
            Id = id,
            ProposalType = proposalType,
            TargetProjectId = targetProjectId,
            TargetAgentId = targetAgentId,
            ProposedAgentName = proposedAgentName,
            ProposedRole = proposedRole,
            Purpose = purpose,
            Evidence = evidence,
            SuggestedChange = suggestedChange,
            SuggestedPrompt = suggestedPrompt,
            SuggestedCapabilities = suggestedCapabilities,
            RiskLevel = riskLevel,
            EvaluationSummary = evaluationSummary,
            Status = status,
            CreatedAt = createdAt,
            ApprovedAt = approvedAt,
            CreatedTaskId = createdTaskId
        };
    }

    private AgentEvolutionProposal()
    {
        TargetProjectId = string.Empty;
        Purpose = string.Empty;
        Evidence = string.Empty;
        SuggestedChange = string.Empty;
    }

    public void BeginEvaluation()
    {
        if (Status != AgentEvolutionProposalStatus.Proposed)
            throw new InvalidOperationException($"Cannot begin evaluation from status '{Status}'.");
        Status = AgentEvolutionProposalStatus.Evaluating;
    }

    public void CompleteEvaluation(string summary)
    {
        if (Status != AgentEvolutionProposalStatus.Evaluating)
            throw new InvalidOperationException($"Cannot complete evaluation from status '{Status}'.");
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("Evaluation summary cannot be empty.", nameof(summary));

        EvaluationSummary = summary;
        Status = AgentEvolutionProposalStatus.AwaitingApproval;
    }

    public void Approve(Guid taskId)
    {
        if (Status != AgentEvolutionProposalStatus.AwaitingApproval)
            throw new InvalidOperationException($"Cannot approve a proposal in status '{Status}'.");

        CreatedTaskId = taskId;
        Status = AgentEvolutionProposalStatus.Approved;
        ApprovedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Populates the target for a pre-target legacy proposal exactly once. This is a compatibility
    /// migration operation, not a general retargeting mechanism.
    /// </summary>
    public void BackfillLegacyTargetProject(string targetProjectId)
    {
        if (!string.IsNullOrWhiteSpace(TargetProjectId))
            throw new InvalidOperationException("Cannot backfill a proposal that already has a target project.");
        if (CreatedTaskId is not null)
            throw new InvalidOperationException("Cannot backfill a proposal that already has an implementation task.");
        if (string.IsNullOrWhiteSpace(targetProjectId))
            throw new ArgumentException("TargetProjectId cannot be empty.", nameof(targetProjectId));

        TargetProjectId = targetProjectId;
    }

    /// <summary>Associates the one implementation task recovered for an already-approved proposal.</summary>
    public void AttachImplementationTask(Guid taskId)
    {
        if (Status != AgentEvolutionProposalStatus.Approved)
            throw new InvalidOperationException($"Cannot attach an implementation task to a proposal in status '{Status}'.");
        if (CreatedTaskId is not null)
            throw new InvalidOperationException("An implementation task is already associated with this proposal.");
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));

        CreatedTaskId = taskId;
    }

    /// <summary>Approves the proposal without an associated implementation task. Approval alone
    /// never activates or creates an agent — it only records human consent. A downstream task
    /// may be created later.</summary>
    public void Approve()
    {
        if (Status is not (AgentEvolutionProposalStatus.AwaitingApproval or AgentEvolutionProposalStatus.Proposed or AgentEvolutionProposalStatus.Evaluating))
            throw new InvalidOperationException($"Cannot approve a proposal in status '{Status}'.");

        Status = AgentEvolutionProposalStatus.Approved;
        ApprovedAt = DateTimeOffset.UtcNow;
    }

    public void Reject()
    {
        if (Status is AgentEvolutionProposalStatus.Implemented or AgentEvolutionProposalStatus.Failed)
            throw new InvalidOperationException($"Cannot reject a proposal in status '{Status}'.");
        Status = AgentEvolutionProposalStatus.Rejected;
    }

    public void MarkImplemented()
    {
        if (Status != AgentEvolutionProposalStatus.Approved)
            throw new InvalidOperationException($"Cannot mark implemented from status '{Status}'.");
        Status = AgentEvolutionProposalStatus.Implemented;
    }

    public void MarkFailed()
    {
        Status = AgentEvolutionProposalStatus.Failed;
    }
}
