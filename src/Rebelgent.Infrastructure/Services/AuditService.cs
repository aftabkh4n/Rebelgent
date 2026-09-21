using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;

namespace Rebelgent.Infrastructure.Services;

/// <summary>
/// Records audit events to the append-only ledger.
/// FAIL-CLOSED: any persistence failure is propagated so callers can roll back the associated
/// privileged action. Never swallows exceptions.
/// Callers requiring privileged actions to be atomic with their audit trail must invoke this
/// service inside an <see cref="IUnitOfWork"/> transaction.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly IAuditRepository _repository;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IAuditRepository repository, ILogger<AuditService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<AuditEvent> RecordAsync(
        string eventType,
        ActorType actorType,
        string actorId,
        string resourceType,
        string resourceId,
        string action,
        object? payload = null,
        CancellationToken ct = default)
    {
        var payloadJson = payload is null ? string.Empty : JsonSerializer.Serialize(payload);
        var latest = await _repository.GetLatestAsync(ct);
        var previousHash = latest?.Hash ?? string.Empty;
        var sequenceNumber = (latest?.SequenceNumber ?? 0) + 1;

        var auditEvent = new AuditEvent(
            sequenceNumber,
            DateTimeOffset.UtcNow,
            eventType,
            actorType,
            actorId,
            resourceType,
            resourceId,
            action,
            payloadJson,
            previousHash);

        try
        {
            return await _repository.AppendAsync(auditEvent, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Audit persistence FAILED for event '{EventType}' at sequence {Sequence}. " +
                "The associated privileged operation must not be committed.",
                eventType, sequenceNumber);
            throw;
        }
    }

    public Task<AuditEvent> RecordSecurityEventAsync(
        string eventType,
        ActorType actorType,
        string actorId,
        string action,
        string details,
        CancellationToken ct = default)
    {
        return RecordAsync(
            eventType,
            actorType,
            actorId,
            "Security",
            string.Empty,
            action,
            new { details },
            ct);
    }
}
