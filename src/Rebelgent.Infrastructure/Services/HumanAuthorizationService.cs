using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;

namespace Rebelgent.Infrastructure.Services;

/// <summary>
/// Enforces that privileged actions are only performed by authenticated humans with the required
/// capability.
///
/// Denial semantics: a denial is always enforced. Denial is the safe outcome.
///
/// Security-event durability: the denial security event is first appended to the canonical
/// <see cref="IAuditRepository"/> ledger. If that fails, the event falls back to the durable
/// <see cref="ISecurityAuditDeadLetterRepository"/> so it cannot silently disappear. A Critical
/// log line is emitted only if BOTH the primary audit ledger AND the dead-letter store fail.
///
/// The audit + dead-letter writes run on a background task inside a FRESH DI scope so they do
/// not race with the caller's scoped DbContext and are not tied to the caller's request lifetime.
/// </summary>
public sealed class HumanAuthorizationService : IHumanAuthorizationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HumanAuthorizationService> _logger;

    public HumanAuthorizationService(
        IServiceScopeFactory scopeFactory,
        ILogger<HumanAuthorizationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void RequireHuman(HumanPrincipal? principal, HumanCapability capability, string action, string resourceId = "")
    {
        if (principal is null)
        {
            FireSecurityEvent(
                AuditEventType.UnauthorizedApprovalAttempt,
                ActorType.Agent,
                "unknown",
                resourceId,
                action,
                $"Attempted privileged action '{action}' on resource '{resourceId}' without authentication.");

            throw new HumanAuthorizationException(
                ActorType.Agent,
                action,
                resourceId,
                AuthorityViolationKind.UnauthorizedApprovalAttempt,
                $"Privileged action '{action}' requires an authenticated human principal.");
        }

        if (!principal.HasCapability(capability))
        {
            FireSecurityEvent(
                AuditEventType.UnauthorizedApprovalAttempt,
                ActorType.Human,
                principal.HumanId.ToString(),
                resourceId,
                action,
                $"Human '{principal.ExternalIdentityId}' lacks capability '{capability}' required for action '{action}'.");

            throw new HumanAuthorizationException(
                ActorType.Human,
                action,
                resourceId,
                AuthorityViolationKind.CapabilityMissing,
                $"Human principal lacks the required capability '{capability}' for action '{action}'.");
        }
    }

    public void RequireHuman(HumanPrincipal? principal, HumanCapability capability, string action, Guid resourceId)
        => RequireHuman(principal, capability, action, resourceId.ToString());

    private void FireSecurityEvent(
        string eventType,
        ActorType actorType,
        string actorId,
        string resourceId,
        string action,
        string details)
    {
        _ = Task.Run(() => PersistSecurityEventAsync(eventType, actorType, actorId, resourceId, action, details));
    }

    // Internal for testing — deterministic await path.
    internal async Task PersistSecurityEventAsync(
        string eventType,
        ActorType actorType,
        string actorId,
        string resourceId,
        string action,
        string details)
    {
        using var scope = _scopeFactory.CreateScope();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        Exception? primaryError = null;
        try
        {
            await auditService.RecordSecurityEventAsync(eventType, actorType, actorId, action, details);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            primaryError = ex;
        }

        // Primary failed — write to dead-letter using a SECOND fresh scope so the failed
        // DbContext state cannot contaminate the fallback write.
        try
        {
            using var fallbackScope = _scopeFactory.CreateScope();
            var deadLetterRepo = fallbackScope.ServiceProvider.GetRequiredService<ISecurityAuditDeadLetterRepository>();

            var payloadJson = JsonSerializer.Serialize(new { details });
            var entry = new SecurityAuditDeadLetter(
                DateTimeOffset.UtcNow,
                eventType,
                actorType,
                actorId,
                "Security",
                resourceId,
                action,
                payloadJson,
                Truncate(primaryError!.ToString(), 4000));
            await deadLetterRepo.AppendAsync(entry);

            _logger.LogError(primaryError,
                "PRIMARY-AUDIT-FAILURE: security event '{EventType}' for action '{Action}' " +
                "persisted to SecurityAuditDeadLetters ({DeadLetterId}).",
                eventType, action, entry.Id);
        }
        catch (Exception fallbackEx) when (fallbackEx is not OperationCanceledException)
        {
            _logger.LogCritical(
                new AggregateException("Both primary audit and dead-letter persistence failed.", primaryError!, fallbackEx),
                "SECURITY-EVENT-LOST: primary audit AND dead-letter both failed. EventType={EventType} Action={Action} ActorId={ActorId} Details={Details}",
                eventType, action, actorId, details);
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : s.Length <= max ? s : s[..max];
}
