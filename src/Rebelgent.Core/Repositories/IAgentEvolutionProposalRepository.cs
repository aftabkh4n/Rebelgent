using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

public interface IAgentEvolutionProposalRepository
{
    Task<AgentEvolutionProposal?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentEvolutionProposal>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AgentEvolutionProposal>> GetByStatusAsync(AgentEvolutionProposalStatus status, CancellationToken ct = default);
    Task<AgentEvolutionProposal> AddAsync(AgentEvolutionProposal proposal, CancellationToken ct = default);
    Task<AgentEvolutionProposal> UpdateAsync(AgentEvolutionProposal proposal, CancellationToken ct = default);
}
