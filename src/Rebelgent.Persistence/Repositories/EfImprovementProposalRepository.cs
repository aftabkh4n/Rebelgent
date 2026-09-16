using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

internal sealed class EfImprovementProposalRepository : IImprovementProposalRepository
{
    private static readonly ImprovementProposalStatus[] TerminalStatuses =
    [
        ImprovementProposalStatus.Rejected,
        ImprovementProposalStatus.Implemented,
        ImprovementProposalStatus.Failed
    ];

    private readonly RebelgentDbContext _db;

    public EfImprovementProposalRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ImprovementProposal proposal, CancellationToken cancellationToken = default)
    {
        var record = ImprovementProposalDbRecord.FromDomain(proposal);
        _db.ImprovementProposals.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ImprovementProposal proposal, CancellationToken cancellationToken = default)
    {
        var record = await _db.ImprovementProposals
            .FirstOrDefaultAsync(p => p.Id == proposal.Id, cancellationToken)
            ?? throw new InvalidOperationException($"ImprovementProposal {proposal.Id} not found in database.");

        record.Status = proposal.Status;
        record.EvaluationSummary = proposal.EvaluationSummary;
        record.CreatedTaskId = proposal.CreatedTaskId;
        record.DecidedAt = proposal.DecidedAt?.UtcTicks;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ImprovementProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await _db.ImprovementProposals.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return record?.ToDomain();
    }

    public async Task<IReadOnlyList<ImprovementProposal>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = await _db.ImprovementProposals
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        return records.Select(r => r.ToDomain()).ToList();
    }

    public async Task<ImprovementProposal?> GetActiveByFingerprintAsync(string evidenceFingerprint, CancellationToken cancellationToken = default)
    {
        var record = await _db.ImprovementProposals
            .Where(p => p.EvidenceFingerprint == evidenceFingerprint && !TerminalStatuses.Contains(p.Status))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return record?.ToDomain();
    }
}
