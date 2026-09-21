using Rebelgent.Core.Authority;
using Rebelgent.Core.Services;

namespace Rebelgent.Telegram.Tests.Fakes;

internal sealed class FakeAuditRecoveryService : IAuditRecoveryService
{
    public List<(Guid Id, HumanPrincipal Human)> RecoverCalls { get; } = [];
    public List<HumanPrincipal> BatchCalls { get; } = [];

    public Func<Guid, HumanPrincipal, AuditRecoveryResult>? RecoverHandler { get; set; }
    public Func<HumanPrincipal, AuditRecoveryBatchResult>? BatchHandler { get; set; }

    public Task<AuditRecoveryResult> RecoverAsync(Guid deadLetterId, HumanPrincipal human, CancellationToken ct = default)
    {
        RecoverCalls.Add((deadLetterId, human));
        var handler = RecoverHandler ?? ((id, _) => new AuditRecoveryResult(true, id, Guid.NewGuid(), false, null));
        return Task.FromResult(handler(deadLetterId, human));
    }

    public Task<AuditRecoveryBatchResult> RecoverAllUnresolvedAsync(HumanPrincipal human, CancellationToken ct = default)
    {
        BatchCalls.Add(human);
        var handler = BatchHandler ?? (_ => new AuditRecoveryBatchResult(0, 0, 0, 0));
        return Task.FromResult(handler(human));
    }
}
