using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;

namespace Rebelgent.Infrastructure.Services;

/// <summary>
/// Manages agent-definition lifecycle transitions with authorization and audit.
/// Every privileged transition is executed inside a single transaction so the domain
/// change and its audit event either both persist or neither persist. Audit persistence
/// failure rolls back the transition — no lifecycle change ever occurs without a durable
/// audit record.
/// </summary>
public sealed class AgentLifecycleService : IAgentLifecycleService
{
    private readonly IHumanAuthorizationService _authService;
    private readonly IAgentDefinitionRepository _agentRepository;
    private readonly IAgentVersionRepository _versionRepository;
    private readonly IAuditService _auditService;
    private readonly IUnitOfWork _unitOfWork;

    public AgentLifecycleService(
        IHumanAuthorizationService authService,
        IAgentDefinitionRepository agentRepository,
        IAgentVersionRepository versionRepository,
        IAuditService auditService,
        IUnitOfWork unitOfWork)
    {
        _authService = authService;
        _agentRepository = agentRepository;
        _versionRepository = versionRepository;
        _auditService = auditService;
        _unitOfWork = unitOfWork;
    }

    public Task<AgentDefinition> ActivateAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default)
    {
        _authService.RequireHuman(human, HumanCapability.ActivateAgent, "Activate", agentId);

        return _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var agent = await GetAgentOrThrowAsync(agentId, token);
            agent.Activate(human);
            await _agentRepository.UpdateAsync(agent, token);
            await _auditService.RecordAsync(
                AuditEventType.AgentActivated,
                ActorType.Human,
                human.HumanId.ToString(),
                "AgentDefinition",
                agentId.ToString(),
                "Activate",
                new { agentId, agentName = agent.Name },
                token);
            return agent;
        }, ct);
    }

    public Task<AgentDefinition> SuspendAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default)
    {
        _authService.RequireHuman(human, HumanCapability.SuspendAgent, "Suspend", agentId);

        return _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var agent = await GetAgentOrThrowAsync(agentId, token);
            agent.Suspend(human);
            await _agentRepository.UpdateAsync(agent, token);
            await _auditService.RecordAsync(
                AuditEventType.AgentSuspended,
                ActorType.Human,
                human.HumanId.ToString(),
                "AgentDefinition",
                agentId.ToString(),
                "Suspend",
                new { agentId, agentName = agent.Name },
                token);
            return agent;
        }, ct);
    }

    public Task<AgentDefinition> RetireAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default)
    {
        _authService.RequireHuman(human, HumanCapability.RetireAgent, "Retire", agentId);

        return _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var agent = await GetAgentOrThrowAsync(agentId, token);
            agent.Retire(human);
            await _agentRepository.UpdateAsync(agent, token);
            await _auditService.RecordAsync(
                AuditEventType.AgentRetired,
                ActorType.Human,
                human.HumanId.ToString(),
                "AgentDefinition",
                agentId.ToString(),
                "Retire",
                new { agentId, agentName = agent.Name },
                token);
            return agent;
        }, ct);
    }

    public async Task<AgentDefinition> GetAsync(Guid agentId, CancellationToken ct = default)
        => await GetAgentOrThrowAsync(agentId, ct);

    public async Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default)
        => await _agentRepository.GetAllAsync(ct);

    private async Task<AgentDefinition> GetAgentOrThrowAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await _agentRepository.GetByIdAsync(agentId, ct);
        if (agent is null)
            throw new InvalidOperationException($"AgentDefinition {agentId} not found.");
        return agent;
    }
}
