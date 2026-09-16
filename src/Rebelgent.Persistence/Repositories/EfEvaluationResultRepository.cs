using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Records;

namespace Rebelgent.Persistence.Repositories;

internal sealed class EfEvaluationResultRepository : IEvaluationResultRepository
{
    private readonly RebelgentDbContext _db;

    public EfEvaluationResultRepository(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(EvaluationResult result, CancellationToken cancellationToken = default)
    {
        var record = EvaluationResultDbRecord.FromDomain(result);
        _db.EvaluationResults.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EvaluationResult>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        var records = await _db.EvaluationResults
            .Where(r => r.ProposalId == proposalId)
            .OrderByDescending(r => r.EvaluatedAt)
            .ToListAsync(cancellationToken);
        return records.Select(r => r.ToDomain()).ToList();
    }
}
