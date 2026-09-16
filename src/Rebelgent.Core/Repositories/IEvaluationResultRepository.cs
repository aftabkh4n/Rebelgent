using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

/// <summary>Provider-independent persistence boundary for <see cref="EvaluationResult"/>.</summary>
public interface IEvaluationResultRepository
{
    Task AddAsync(EvaluationResult result, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvaluationResult>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);
}
