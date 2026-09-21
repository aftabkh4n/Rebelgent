using Microsoft.Extensions.Logging;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;

namespace Rebelgent.Infrastructure.Services;

/// <summary>
/// Repairs the audit log only. Given a <see cref="SecurityAuditDeadLetter"/>, appends a canonical
/// <see cref="AuditEvent"/> reconstructed from the dead-letter's fields, then appends a
/// <see cref="SecurityAuditDeadLetterRecovery"/> linkage row referencing both. Both writes happen
/// inside a single <see cref="IUnitOfWork"/> transaction so either both persist or neither does.
///
/// Recovery NEVER replays the underlying denied privileged action. It never approves anything,
/// never activates, suspends, retires, or otherwise changes application state beyond audit rows.
/// </summary>
public sealed class AuditRecoveryService : IAuditRecoveryService
{
    private readonly ISecurityAuditDeadLetterRepository _deadLetterRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly IHumanAuthorizationService _authService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AuditRecoveryService> _logger;

    public AuditRecoveryService(
        ISecurityAuditDeadLetterRepository deadLetterRepository,
        IAuditRepository auditRepository,
        IHumanAuthorizationService authService,
        IUnitOfWork unitOfWork,
        ILogger<AuditRecoveryService> logger)
    {
        _deadLetterRepository = deadLetterRepository;
        _auditRepository = auditRepository;
        _authService = authService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AuditRecoveryResult> RecoverAsync(Guid deadLetterId, HumanPrincipal human, CancellationToken ct = default)
    {
        _authService.RequireHuman(human, HumanCapability.ChangeSecurityPolicy, "RecoverAudit", deadLetterId);

        var deadLetter = await _deadLetterRepository.GetByIdAsync(deadLetterId, ct);
        if (deadLetter is null)
        {
            return new AuditRecoveryResult(false, deadLetterId, null, false, "Dead-letter entry not found.");
        }

        // Idempotency: if any recovery row already links this dead-letter, do not create another.
        var existing = await _deadLetterRepository.GetRecoveriesAsync(deadLetterId, ct);
        if (existing.Count > 0)
        {
            return new AuditRecoveryResult(true, deadLetterId, existing[0].RecoveredAuditEventId, true, "Already resolved.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Double-check inside the transaction to close any race with a concurrent recovery.
            var raceCheck = await _deadLetterRepository.GetRecoveriesAsync(deadLetterId, token);
            if (raceCheck.Count > 0)
            {
                return new AuditRecoveryResult(true, deadLetterId, raceCheck[0].RecoveredAuditEventId, true, "Already resolved.");
            }

            var latest = await _auditRepository.GetLatestAsync(token);
            var previousHash = latest?.Hash ?? string.Empty;
            var sequenceNumber = (latest?.SequenceNumber ?? 0) + 1;

            var canonical = new AuditEvent(
                sequenceNumber,
                deadLetter.TimestampUtc, // preserve the ORIGINAL evidence timestamp
                deadLetter.EventType,
                deadLetter.ActorType,
                deadLetter.ActorId,
                deadLetter.ResourceType,
                deadLetter.ResourceId,
                deadLetter.Action,
                deadLetter.PayloadJson,
                previousHash);

            await _auditRepository.AppendAsync(canonical, token);

            var recoveryLink = new SecurityAuditDeadLetterRecovery(
                deadLetter.Id,
                canonical.Id,
                human.HumanId);
            await _deadLetterRepository.AppendRecoveryAsync(recoveryLink, token);

            _logger.LogInformation(
                "Recovered SecurityAuditDeadLetter {DeadLetterId} into canonical AuditEvent {AuditEventId} at sequence {Sequence}.",
                deadLetter.Id, canonical.Id, canonical.SequenceNumber);

            return new AuditRecoveryResult(true, deadLetterId, canonical.Id, false, null);
        }, ct);
    }

    public async Task<AuditRecoveryBatchResult> RecoverAllUnresolvedAsync(HumanPrincipal human, CancellationToken ct = default)
    {
        _authService.RequireHuman(human, HumanCapability.ChangeSecurityPolicy, "RecoverAuditBatch", string.Empty);

        var unresolved = await _deadLetterRepository.GetUnresolvedAsync(ct);
        int attempted = 0, recoveredNow = 0, alreadyResolved = 0, failed = 0;

        foreach (var entry in unresolved)
        {
            attempted++;
            try
            {
                var result = await RecoverAsync(entry.Id, human, ct);
                if (!result.Succeeded) failed++;
                else if (result.WasAlreadyResolved) alreadyResolved++;
                else recoveredNow++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Recovery failed for dead-letter {DeadLetterId}.", entry.Id);
                failed++;
            }
        }

        return new AuditRecoveryBatchResult(attempted, recoveredNow, alreadyResolved, failed);
    }
}
