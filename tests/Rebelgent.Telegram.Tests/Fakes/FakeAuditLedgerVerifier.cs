using Rebelgent.Core.Audit;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeAuditLedgerVerifier : IAuditLedgerVerifier
{
    public AuditLedgerVerificationResult Result { get; set; } = new()
    {
        IsValid = true,
        VerifiedEventCount = 0
    };

    public Task<AuditLedgerVerificationResult> VerifyAsync(CancellationToken ct = default)
        => Task.FromResult(Result);
}
