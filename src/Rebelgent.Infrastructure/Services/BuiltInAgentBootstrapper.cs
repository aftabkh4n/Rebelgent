using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;

namespace Rebelgent.Infrastructure.Services;

/// <summary>
/// Imports the built-in Rebelgent agents (Developer, QA Engineer, Code Reviewer,
/// Release Manager, Improvement Analyst, Agent Evolution Manager) into the governed
/// <see cref="AgentDefinition"/> registry with deterministic stable IDs.
///
/// COMPATIBILITY BOOTSTRAP ONLY: these agents already existed and were operational
/// pre-M10. They are marked Active without a <see cref="HumanPrincipal"/> because
/// they were never proposed through the M10 evolution pipeline. This exception
/// exists ONLY for pre-existing built-ins. Newly proposed agents still require
/// human authorization via <see cref="IAgentLifecycleService.ActivateAsync"/>.
///
/// Idempotency: stable per-agent GUID derived from
/// <c>MD5("rebelgent:builtin:agent:{name}")</c>. A restart that finds the row
/// already present makes no changes and emits no new audit event for that agent.
/// </summary>
public sealed class BuiltInAgentBootstrapper : IBuiltInAgentBootstrapper
{
    private readonly IAgentDefinitionRepository _agentRepository;
    private readonly IAgentVersionRepository _versionRepository;
    private readonly IAuditService _auditService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BuiltInAgentBootstrapper> _logger;

    // Fixed pre-M10 built-in agents. Never remove entries — retiring a built-in is
    // still a governed lifecycle transition on the stored AgentDefinition.
    internal static readonly BuiltInAgent[] BuiltIns =
    [
        new("Developer", AgentRole.BackendDeveloper,
            "Implements assigned tasks in isolated git worktrees.",
            "Reads task, plans changes, edits code, runs build and tests, commits on task branch. Never merges, pushes, or self-approves."),
        new("QA Engineer", AgentRole.QaEngineer,
            "Independently verifies developer output.",
            "Attempts to find failures and regressions. Does not implement fixes."),
        new("Code Reviewer", AgentRole.CodeReviewer,
            "Reviews developer changes for correctness, safety and style.",
            "Independent from the implementer. Does not merge."),
        new("Release Manager", AgentRole.ReleaseManager,
            "Drafts release notes from merged work.",
            "Never publishes a release without explicit human approval."),
        new("Improvement Analyst", AgentRole.ImprovementAnalyst,
            "Analyses failure history and drafts improvement proposals.",
            "Read-only Claude Code invocation with zero tools. Never modifies code or approves proposals."),
        new("Agent Evolution Manager", AgentRole.EvolutionManager,
            "Analyses per-agent performance and drafts agent-evolution proposals.",
            "Read-only Claude Code invocation with zero tools. Cannot create, activate, suspend, or retire any agent — proposals only.")
    ];

    // Well-known synthetic "system bootstrap" human ID. Not an authenticated human;
    // used only as CreatedByHumanId on imported built-ins so the non-nullable field
    // has a stable, recognisable value. It corresponds to no real principal and is
    // NEVER granted capabilities.
    internal static readonly Guid SystemBootstrapActorId =
        DeterministicGuid("rebelgent:builtin:actor:system-bootstrap");

    public BuiltInAgentBootstrapper(
        IAgentDefinitionRepository agentRepository,
        IAgentVersionRepository versionRepository,
        IAuditService auditService,
        IUnitOfWork unitOfWork,
        ILogger<BuiltInAgentBootstrapper> logger)
    {
        _agentRepository = agentRepository;
        _versionRepository = versionRepository;
        _auditService = auditService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<BuiltInAgentBootstrapResult> EnsureBootstrappedAsync(CancellationToken ct = default)
    {
        int imported = 0;
        int alreadyPresent = 0;

        foreach (var builtIn in BuiltIns)
        {
            var agentId = builtIn.AgentId;
            var existing = await _agentRepository.GetByIdAsync(agentId, ct);
            if (existing is not null)
            {
                alreadyPresent++;
                continue;
            }

            await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                // Re-check inside the transaction to avoid duplicate imports under a race.
                var raceCheck = await _agentRepository.GetByIdAsync(agentId, token);
                if (raceCheck is not null)
                    return;

                var version = AgentVersion.Reconstitute(
                    id: builtIn.VersionId,
                    agentDefinitionId: agentId,
                    version: "1.0.0",
                    promptTemplate: builtIn.Description,
                    capabilities: "builtin",
                    providerConfigurationReference: null,
                    createdAt: DateTimeOffset.UtcNow,
                    createdByProposalId: null,
                    evaluationSummary: "Built-in bootstrap: pre-M10 operational agent.",
                    status: AgentVersionStatus.Active);
                await _versionRepository.AddAsync(version, token);

                var definition = AgentDefinition.Reconstitute(
                    id: agentId,
                    name: builtIn.Name,
                    role: builtIn.Role,
                    purpose: builtIn.Purpose,
                    description: builtIn.Description,
                    status: AgentLifecycleStatus.Active,
                    currentVersionId: builtIn.VersionId,
                    createdAt: DateTimeOffset.UtcNow,
                    createdByHumanId: SystemBootstrapActorId,
                    activatedAt: DateTimeOffset.UtcNow,
                    suspendedAt: null,
                    retiredAt: null);
                await _agentRepository.AddAsync(definition, token);

                await _auditService.RecordAsync(
                    AuditEventType.BuiltInAgentImported,
                    ActorType.System,
                    "BuiltInAgentBootstrapper",
                    "AgentDefinition",
                    agentId.ToString(),
                    "Import",
                    new
                    {
                        agentId,
                        versionId = builtIn.VersionId,
                        name = builtIn.Name,
                        role = builtIn.Role.ToString(),
                        note = "Compatibility import of pre-M10 built-in agent. Not a human-authorized activation."
                    },
                    token);
            }, ct);

            imported++;
            _logger.LogInformation("Imported built-in agent '{Name}' ({AgentId}).", builtIn.Name, agentId);
        }

        return new BuiltInAgentBootstrapResult(BuiltIns.Length, imported, alreadyPresent);
    }

    internal sealed record BuiltInAgent(string Name, AgentRole Role, string Purpose, string Description)
    {
        public Guid AgentId => DeterministicGuid($"rebelgent:builtin:agent:{Name}");
        public Guid VersionId => DeterministicGuid($"rebelgent:builtin:version:{Name}:1.0.0");
    }

    internal static Guid DeterministicGuid(string key)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(bytes);
    }
}
