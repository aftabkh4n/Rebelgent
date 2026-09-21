using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Infrastructure.Services;

namespace Rebelgent.Core.Tests.Audit;

/// <summary>
/// Proves that <see cref="AuditService.RecordAsync"/> is fail-closed. A persistence failure
/// must propagate so callers running inside a transaction can roll back the associated
/// privileged operation. The old behaviour (log warning + return placeholder) is gone.
/// </summary>
public class AuditServiceFailClosedTests
{
    private sealed class ThrowingAuditRepository : IAuditRepository
    {
        public Exception ExceptionToThrow { get; set; } = new InvalidOperationException("simulated persistence failure");
        public Task<AuditEvent> AppendAsync(AuditEvent auditEvent, CancellationToken ct = default) => throw ExceptionToThrow;
        public Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default) => Task.FromResult<AuditEvent?>(null);
        public Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    [Fact]
    public async Task RecordAsync_AppendThrows_ExceptionPropagates()
    {
        var repo = new ThrowingAuditRepository();
        var service = new AuditService(repo, NullLogger<AuditService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordAsync(
                AuditEventType.AgentActivated,
                ActorType.Human,
                "actor-1",
                "AgentDefinition",
                "res-1",
                "Activate"));

        Assert.Equal("simulated persistence failure", ex.Message);
    }

    [Fact]
    public async Task RecordAsync_DoesNotReturnPlaceholderOnFailure()
    {
        // The old fault-tolerant path returned a synthetic AuditEvent when persistence failed.
        // The current contract is: throw. No placeholder ever reaches the caller.
        var repo = new ThrowingAuditRepository();
        var service = new AuditService(repo, NullLogger<AuditService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordAsync(
                AuditEventType.AgentSuspended,
                ActorType.Human,
                "actor-1",
                "AgentDefinition",
                "res-1",
                "Suspend"));
    }

    [Fact]
    public async Task RecordSecurityEventAsync_AppendThrows_ExceptionPropagates()
    {
        var repo = new ThrowingAuditRepository();
        var service = new AuditService(repo, NullLogger<AuditService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordSecurityEventAsync(
                AuditEventType.UnauthorizedApprovalAttempt,
                ActorType.Agent,
                "unknown",
                "Activate",
                "details"));
    }

    [Fact]
    public async Task RecordAsync_OperationCanceled_PropagatesWithoutWrapping()
    {
        var repo = new ThrowingAuditRepository { ExceptionToThrow = new OperationCanceledException() };
        var service = new AuditService(repo, NullLogger<AuditService>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RecordAsync(
                AuditEventType.TaskCreated,
                ActorType.System,
                "sys",
                "AgentTask",
                "task-1",
                "Create"));
    }
}
