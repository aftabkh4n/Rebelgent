using Rebelgent.Core.Audit;

namespace Rebelgent.Core.Repositories;

/// <summary>
/// Append-only repository for security-audit dead-letter records and their recovery linkage rows.
/// No Update or Delete methods are exposed. Neither the dead-letter payload nor the recovery
/// linkage can be modified once written.
/// </summary>
public interface ISecurityAuditDeadLetterRepository
{
    Task<SecurityAuditDeadLetter> AppendAsync(SecurityAuditDeadLetter entry, CancellationToken ct = default);
    Task<SecurityAuditDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityAuditDeadLetter>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Dead-letter records that have no recovery linkage row yet.</summary>
    Task<IReadOnlyList<SecurityAuditDeadLetter>> GetUnresolvedAsync(CancellationToken ct = default);

    /// <summary>Count of dead-letter records without any recovery linkage.</summary>
    Task<int> CountUnresolvedAsync(CancellationToken ct = default);

    Task<SecurityAuditDeadLetterRecovery> AppendRecoveryAsync(SecurityAuditDeadLetterRecovery recovery, CancellationToken ct = default);

    Task<IReadOnlyList<SecurityAuditDeadLetterRecovery>> GetRecoveriesAsync(Guid deadLetterId, CancellationToken ct = default);
}
