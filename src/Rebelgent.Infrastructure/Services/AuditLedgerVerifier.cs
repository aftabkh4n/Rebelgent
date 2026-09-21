using Rebelgent.Core.Audit;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;

namespace Rebelgent.Infrastructure.Services;

/// <summary>
/// Verifies the integrity of the tamper-evident audit ledger by recomputing and comparing
/// hash chains across all stored events.
/// </summary>
public sealed class AuditLedgerVerifier : IAuditLedgerVerifier
{
    private readonly IAuditRepository _repository;

    public AuditLedgerVerifier(IAuditRepository repository)
    {
        _repository = repository;
    }

    public async Task<AuditLedgerVerificationResult> VerifyAsync(CancellationToken ct = default)
    {
        var events = await _repository.GetAllOrderedAsync(ct);

        if (events.Count == 0)
        {
            return new AuditLedgerVerificationResult
            {
                IsValid = true,
                VerifiedEventCount = 0
            };
        }

        string expectedPreviousHash = string.Empty;
        long expectedSequenceNumber = 1;

        foreach (var auditEvent in events)
        {
            // Verify sequence number is contiguous
            if (auditEvent.SequenceNumber != expectedSequenceNumber)
            {
                return new AuditLedgerVerificationResult
                {
                    IsValid = false,
                    VerifiedEventCount = expectedSequenceNumber - 1,
                    FailureReason = $"Sequence number gap: expected {expectedSequenceNumber}, found {auditEvent.SequenceNumber}.",
                    FirstFailingSequenceNumber = auditEvent.SequenceNumber
                };
            }

            // Verify previous hash matches
            if (auditEvent.PreviousHash != expectedPreviousHash)
            {
                return new AuditLedgerVerificationResult
                {
                    IsValid = false,
                    VerifiedEventCount = expectedSequenceNumber - 1,
                    FailureReason = $"PreviousHash mismatch at sequence {auditEvent.SequenceNumber}.",
                    FirstFailingSequenceNumber = auditEvent.SequenceNumber
                };
            }

            // Recompute and verify hash
            var recomputedHash = AuditEvent.ComputeHash(
                auditEvent.SequenceNumber,
                auditEvent.TimestampUtc,
                auditEvent.EventType,
                auditEvent.ActorType,
                auditEvent.ActorId,
                auditEvent.ResourceType,
                auditEvent.ResourceId,
                auditEvent.Action,
                auditEvent.PayloadJson,
                auditEvent.PreviousHash);

            if (auditEvent.Hash != recomputedHash)
            {
                return new AuditLedgerVerificationResult
                {
                    IsValid = false,
                    VerifiedEventCount = expectedSequenceNumber - 1,
                    FailureReason = $"Hash mismatch at sequence {auditEvent.SequenceNumber}: stored hash does not match recomputed hash.",
                    FirstFailingSequenceNumber = auditEvent.SequenceNumber
                };
            }

            expectedPreviousHash = auditEvent.Hash;
            expectedSequenceNumber++;
        }

        return new AuditLedgerVerificationResult
        {
            IsValid = true,
            VerifiedEventCount = events.Count
        };
    }
}
