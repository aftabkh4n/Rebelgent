namespace Rebelgent.Core.Audit;

/// <summary>Verifies the integrity of the tamper-evident audit ledger hash chain.</summary>
public interface IAuditLedgerVerifier
{
    Task<AuditLedgerVerificationResult> VerifyAsync(CancellationToken ct = default);
}
