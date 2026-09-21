using Rebelgent.Core.Audit;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Telegram.Tests.Fakes;

internal sealed class FakeSecurityAuditDeadLetterRepository : ISecurityAuditDeadLetterRepository
{
    public List<SecurityAuditDeadLetter> Entries { get; } = [];
    public List<SecurityAuditDeadLetterRecovery> Recoveries { get; } = [];

    public Task<SecurityAuditDeadLetter> AppendAsync(SecurityAuditDeadLetter entry, CancellationToken ct = default)
    { Entries.Add(entry); return Task.FromResult(entry); }
    public Task<SecurityAuditDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));
    public Task<IReadOnlyList<SecurityAuditDeadLetter>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SecurityAuditDeadLetter>>(Entries.ToList());
    public Task<IReadOnlyList<SecurityAuditDeadLetter>> GetUnresolvedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SecurityAuditDeadLetter>>(Entries.Where(e => Recoveries.All(r => r.DeadLetterId != e.Id)).ToList());
    public Task<int> CountUnresolvedAsync(CancellationToken ct = default)
        => Task.FromResult(Entries.Count(e => Recoveries.All(r => r.DeadLetterId != e.Id)));
    public Task<SecurityAuditDeadLetterRecovery> AppendRecoveryAsync(SecurityAuditDeadLetterRecovery recovery, CancellationToken ct = default)
    { Recoveries.Add(recovery); return Task.FromResult(recovery); }
    public Task<IReadOnlyList<SecurityAuditDeadLetterRecovery>> GetRecoveriesAsync(Guid deadLetterId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SecurityAuditDeadLetterRecovery>>(Recoveries.Where(r => r.DeadLetterId == deadLetterId).ToList());
}
