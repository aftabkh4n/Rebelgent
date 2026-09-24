using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Projects;

namespace Rebelgent.ClaudeCode.Evolution;

/// <summary>
/// Startup-time reconciler that closes the evolution lifecycle using only persisted, governed
/// evidence. It never grants a capability, never activates or retires an agent, and never
/// accepts model-supplied text as implementation proof. Every mutation is wrapped in
/// <see cref="IUnitOfWork.ExecuteInTransactionAsync"/> together with its audit event so that
/// audit failure rolls back the state change.
/// </summary>
public sealed class EvolutionLifecycleReconciler : IEvolutionLifecycleReconciler
{
    private readonly IAgentEvolutionProposalRepository _proposalRepository;
    private readonly IAgentTaskRepository _taskRepository;
    private readonly IAuditService _auditService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProjectRegistry _projectRegistry;
    private readonly IOptions<AgentEvolutionOptions> _options;
    private readonly ILogger<EvolutionLifecycleReconciler> _logger;

    private const string SystemActorId = "EvolutionLifecycleReconciler";

    public EvolutionLifecycleReconciler(
        IAgentEvolutionProposalRepository proposalRepository,
        IAgentTaskRepository taskRepository,
        IAuditService auditService,
        IUnitOfWork unitOfWork,
        IProjectRegistry projectRegistry,
        IOptions<AgentEvolutionOptions> options,
        ILogger<EvolutionLifecycleReconciler> logger)
    {
        _proposalRepository = proposalRepository;
        _taskRepository = taskRepository;
        _auditService = auditService;
        _unitOfWork = unitOfWork;
        _projectRegistry = projectRegistry;
        _options = options;
        _logger = logger;
    }

    public async Task<EvolutionLifecycleReconciliationResult> ReconcileAsync(CancellationToken ct = default)
    {
        var repaired = 0;
        var implemented = 0;
        var skipped = 0;

        var allProposals = await _proposalRepository.GetAllAsync(ct);

        foreach (var proposal in allProposals)
        {
            if (await TryRepairTargetProjectAsync(proposal, ct))
                repaired++;
        }

        var reloaded = await _proposalRepository.GetAllAsync(ct);
        foreach (var proposal in reloaded)
        {
            await TryBeginImplementationAsync(proposal, ct);
            var current = await _proposalRepository.GetByIdAsync(proposal.Id, ct) ?? proposal;
            var outcome = await TryMarkImplementedAsync(current, ct);
            switch (outcome)
            {
                case ImplementedOutcome.Applied: implemented++; break;
                case ImplementedOutcome.Skipped: skipped++; break;
                case ImplementedOutcome.NotEligible: break;
            }
        }

        _logger.LogInformation(
            "Evolution lifecycle reconciliation complete. Repaired={Repaired}, Implemented={Implemented}, Skipped={Skipped}.",
            repaired, implemented, skipped);

        return new EvolutionLifecycleReconciliationResult(repaired, implemented, skipped);
    }

    private async Task TryBeginImplementationAsync(AgentEvolutionProposal proposal, CancellationToken ct)
    {
        if (proposal.Status != AgentEvolutionProposalStatus.Approved || proposal.CreatedTaskId is null)
            return;

        var task = await _taskRepository.GetByIdAsync(proposal.CreatedTaskId.Value, ct);
        if (task is null || task.Status is AgentTaskStatus.Created or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled)
            return;
        if (task.Status is (AgentTaskStatus.AwaitingReview or AgentTaskStatus.Completed)
            && !string.IsNullOrWhiteSpace(task.MergeCommitSha)
            && task.MergedAt is not null)
            return;

        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var fresh = await _proposalRepository.GetByIdAsync(proposal.Id, token);
            if (fresh is null || fresh.Status != AgentEvolutionProposalStatus.Approved)
                return;

            fresh.BeginImplementation();
            await _proposalRepository.UpdateAsync(fresh, token);
            await _auditService.RecordAsync(
                AuditEventType.AgentEvolutionImplementationStarted,
                ActorType.System,
                SystemActorId,
                "AgentEvolutionProposal",
                fresh.Id.ToString(),
                "BeginImplementation",
                new
                {
                    proposalId = fresh.Id,
                    createdTaskId = fresh.CreatedTaskId,
                    targetProjectId = fresh.TargetProjectId,
                    taskStatus = task.Status.ToString()
                },
                token);
        }, ct);
    }

    private async Task<bool> TryRepairTargetProjectAsync(AgentEvolutionProposal proposal, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(proposal.TargetProjectId))
            return false;

        if (proposal.Status is AgentEvolutionProposalStatus.Rejected)
            return false;

        var configuredProjectId = _options.Value.ProjectId;
        if (string.IsNullOrWhiteSpace(configuredProjectId)
            || string.Equals(configuredProjectId, "default", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(configuredProjectId)
            || configuredProjectId.Contains(Path.DirectorySeparatorChar)
            || configuredProjectId.Contains(Path.AltDirectorySeparatorChar))
        {
            _logger.LogWarning(
                "Skipping TargetProjectId repair for proposal {ProposalId}: AgentEvolution:ProjectId is invalid or not configured.",
                proposal.Id);
            return false;
        }
        if (_projectRegistry.Find(configuredProjectId) is null)
        {
            _logger.LogWarning(
                "Skipping TargetProjectId repair for proposal {ProposalId}: configured project '{ProjectId}' is not registered.",
                proposal.Id, configuredProjectId);
            return false;
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var fresh = await _proposalRepository.GetByIdAsync(proposal.Id, token);
            if (fresh is null || !string.IsNullOrWhiteSpace(fresh.TargetProjectId))
                return false;

            fresh.RepairPersistedTargetProject(configuredProjectId);
            await _proposalRepository.UpdateAsync(fresh, token);
            await _auditService.RecordAsync(
                AuditEventType.EvolutionTargetProjectRepaired,
                ActorType.System,
                SystemActorId,
                "AgentEvolutionProposal",
                fresh.Id.ToString(),
                "RepairTargetProject",
                new
                {
                    proposalId = fresh.Id,
                    oldTargetProjectId = string.Empty,
                    targetProjectId = configuredProjectId,
                    reason = "persisted TargetProjectId was blank after prior approval; " +
                        "restored from AgentEvolution:ProjectId with registry validation"
                },
                token);

            _logger.LogInformation(
                "Repaired persisted TargetProjectId for proposal {ProposalId} to '{ProjectId}'.",
                fresh.Id, configuredProjectId);
            return true;
        }, ct);
    }

    private async Task<ImplementedOutcome> TryMarkImplementedAsync(AgentEvolutionProposal proposal, CancellationToken ct)
    {
        if (proposal.Status is not (AgentEvolutionProposalStatus.Approved or AgentEvolutionProposalStatus.Implementing))
            return ImplementedOutcome.NotEligible;

        if (proposal.CreatedTaskId is null)
            return ImplementedOutcome.Skipped;

        var task = await _taskRepository.GetByIdAsync(proposal.CreatedTaskId.Value, ct);
        if (task is null)
            return ImplementedOutcome.Skipped;

        // A merge can be performed from AwaitingReview or Completed in the normal governed
        // pipeline. The persisted merge fields, not model text or status alone, are the proof.
        if (task.Status is not (AgentTaskStatus.AwaitingReview or AgentTaskStatus.Completed))
            return ImplementedOutcome.Skipped;

        if (string.IsNullOrWhiteSpace(task.MergeCommitSha) || task.MergedAt is null)
            return ImplementedOutcome.Skipped;

        var mergedAt = task.MergedAt ?? DateTimeOffset.UtcNow;
        var mergeSha = task.MergeCommitSha!;
        var pr = task.PullRequestNumber;

        var applied = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var fresh = await _proposalRepository.GetByIdAsync(proposal.Id, token);
            if (fresh is null)
                return false;
            if (fresh.Status is AgentEvolutionProposalStatus.Implemented)
                return false;
            if (fresh.Status is not (AgentEvolutionProposalStatus.Approved or AgentEvolutionProposalStatus.Implementing))
                return false;
            if (fresh.CreatedTaskId != proposal.CreatedTaskId)
                return false;

            fresh.MarkImplemented(mergeSha, pr, mergedAt);
            await _proposalRepository.UpdateAsync(fresh, token);
            await _auditService.RecordAsync(
                AuditEventType.AgentEvolutionImplemented,
                ActorType.System,
                SystemActorId,
                "AgentEvolutionProposal",
                fresh.Id.ToString(),
                "MarkImplemented",
                new
                {
                    proposalId = fresh.Id,
                    createdTaskId = fresh.CreatedTaskId,
                    targetProjectId = fresh.TargetProjectId,
                    pullRequestNumber = pr,
                    mergeCommitSha = mergeSha,
                    implementedAt = mergedAt
                },
                token);
            return true;
        }, ct);

        return applied ? ImplementedOutcome.Applied : ImplementedOutcome.Skipped;
    }

    private enum ImplementedOutcome
    {
        NotEligible,
        Applied,
        Skipped
    }
}
