using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

/// <summary>Provider-independent persistence boundary for <see cref="ImprovementProposal"/>.</summary>
public interface IImprovementProposalRepository
{
    Task AddAsync(ImprovementProposal proposal, CancellationToken cancellationToken = default);

    Task UpdateAsync(ImprovementProposal proposal, CancellationToken cancellationToken = default);

    Task<ImprovementProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImprovementProposal>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns any non-terminal (Proposed/Evaluating/AwaitingApproval/Approved) proposal
    /// with the given evidence fingerprint, used to prevent duplicate proposals for the same
    /// unresolved issue. Terminal proposals (Rejected/Implemented/Failed) are excluded so the
    /// issue can be re-proposed if it recurs after a prior proposal was rejected or closed out.</summary>
    Task<ImprovementProposal?> GetActiveByFingerprintAsync(string evidenceFingerprint, CancellationToken cancellationToken = default);
}
