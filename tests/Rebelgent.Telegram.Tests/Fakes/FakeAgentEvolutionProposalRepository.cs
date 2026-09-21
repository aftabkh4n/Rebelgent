using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeAgentEvolutionProposalRepository : IAgentEvolutionProposalRepository
{
    public List<AgentEvolutionProposal> Proposals { get; set; } = [];

    public Task<AgentEvolutionProposal?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Proposals.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<AgentEvolutionProposal>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentEvolutionProposal>>(Proposals);

    public Task<IReadOnlyList<AgentEvolutionProposal>> GetByStatusAsync(AgentEvolutionProposalStatus status, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentEvolutionProposal>>(Proposals.Where(p => p.Status == status).ToList());

    public Task<AgentEvolutionProposal> AddAsync(AgentEvolutionProposal proposal, CancellationToken ct = default)
    {
        Proposals.Add(proposal);
        return Task.FromResult(proposal);
    }

    public Task<AgentEvolutionProposal> UpdateAsync(AgentEvolutionProposal proposal, CancellationToken ct = default)
    {
        var idx = Proposals.FindIndex(p => p.Id == proposal.Id);
        if (idx >= 0) Proposals[idx] = proposal;
        return Task.FromResult(proposal);
    }
}
