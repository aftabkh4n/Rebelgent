using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

/// <summary>
/// Append-only repository for approval records.
/// No Update or Delete methods are exposed by design.
/// </summary>
public interface IApprovalRepository
{
    Task<ApprovalRecord> AddAsync(ApprovalRecord record, CancellationToken ct = default);
    Task<ApprovalRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApprovalRecord?> FindAsync(string actionType, string resourceId, string humanId, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalRecord>> GetForResourceAsync(string resourceId, CancellationToken ct = default);
}
