using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

internal sealed class EfAgentEvolutionProposalRepository : IAgentEvolutionProposalRepository
{
    private readonly RebelgentDbContext _db;

    public EfAgentEvolutionProposalRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task<AgentEvolutionProposal?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await _db.AgentEvolutionProposals.FirstOrDefaultAsync(p => p.Id == id, ct);
        return record?.ToDomain();
    }

    public async Task<IReadOnlyList<AgentEvolutionProposal>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await _db.AgentEvolutionProposals
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<AgentEvolutionProposal>> GetByStatusAsync(AgentEvolutionProposalStatus status, CancellationToken ct = default)
    {
        var statusInt = (int)status;
        var records = await _db.AgentEvolutionProposals
            .Where(p => p.Status == statusInt)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<AgentEvolutionProposal> AddAsync(AgentEvolutionProposal proposal, CancellationToken ct = default)
    {
        var record = AgentEvolutionProposalDbRecord.FromDomain(proposal);
        _db.AgentEvolutionProposals.Add(record);
        await _db.SaveChangesAsync(ct);
        return proposal;
    }

    public async Task<AgentEvolutionProposal> UpdateAsync(AgentEvolutionProposal proposal, CancellationToken ct = default)
    {
        var record = await _db.AgentEvolutionProposals
            .FirstOrDefaultAsync(p => p.Id == proposal.Id, ct)
            ?? throw new InvalidOperationException($"AgentEvolutionProposal {proposal.Id} not found in database.");

        record.Status = (int)proposal.Status;
        record.EvaluationSummary = proposal.EvaluationSummary;
        record.ApprovedAt = proposal.ApprovedAt?.UtcTicks;
        record.CreatedTaskId = proposal.CreatedTaskId;
        record.TargetProjectId = proposal.TargetProjectId;
        record.ImplementationMergeCommitSha = proposal.ImplementationMergeCommitSha;
        record.ImplementationPullRequestNumber = proposal.ImplementationPullRequestNumber;
        record.ImplementedAt = proposal.ImplementedAt?.UtcTicks;

        await _db.SaveChangesAsync(ct);
        return proposal;
    }
}
