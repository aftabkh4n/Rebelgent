namespace Rebelgent.Core.Audit;

/// <summary>Result of verifying the integrity of the audit ledger hash chain.</summary>
public sealed class AuditLedgerVerificationResult
{
    public bool IsValid { get; init; }
    public long VerifiedEventCount { get; init; }
    public string? FailureReason { get; init; }
    public long? FirstFailingSequenceNumber { get; init; }
}
