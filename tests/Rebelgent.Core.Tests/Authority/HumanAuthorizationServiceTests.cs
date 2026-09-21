using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Infrastructure.Services;

namespace Rebelgent.Core.Tests.Authority;

public class HumanAuthorizationServiceTests
{
    private sealed class InMemoryAuditRepository : IAuditRepository
    {
        private readonly List<AuditEvent> _events = [];

        public Task<AuditEvent> AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            _events.Add(auditEvent);
            return Task.FromResult(auditEvent);
        }

        public Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default)
            => Task.FromResult(_events.Count > 0 ? _events[^1] : (AuditEvent?)null);

        public Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(_events.OrderBy(e => e.SequenceNumber).ToList());

        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(_events.OrderByDescending(e => e.SequenceNumber).Take(count).ToList());
    }

    private sealed class InMemoryDeadLetterRepository : ISecurityAuditDeadLetterRepository
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

    private static HumanAuthorizationService BuildService()
    {
        var scopeFactory = SilentAuditScopeFactoryBuilder.Build();
        return new HumanAuthorizationService(scopeFactory, NullLogger<HumanAuthorizationService>.Instance);
    }

    private static HumanPrincipal MakePrincipal(params HumanCapability[] capabilities) =>
        new(Guid.NewGuid(), "Test", "user-1", capabilities.ToHashSet(), DateTimeOffset.UtcNow);

    [Fact]
    public void RequireHuman_NullPrincipal_ThrowsHumanAuthorizationException()
    {
        var service = BuildService();

        var ex = Assert.Throws<HumanAuthorizationException>(() =>
            service.RequireHuman(null, HumanCapability.ActivateAgent, "Activate", "resource-1"));

        Assert.Equal(AuthorityViolationKind.UnauthorizedApprovalAttempt, ex.ViolationKind);
        Assert.Equal(ActorType.Agent, ex.ActorType);
    }

    [Fact]
    public void RequireHuman_PrincipalWithCapability_DoesNotThrow()
    {
        var service = BuildService();
        var principal = MakePrincipal(HumanCapability.ActivateAgent);

        var ex = Record.Exception(() =>
            service.RequireHuman(principal, HumanCapability.ActivateAgent, "Activate", "resource-1"));

        Assert.Null(ex);
    }

    [Fact]
    public void RequireHuman_PrincipalLacksCapability_ThrowsWithCapabilityMissingKind()
    {
        var service = BuildService();
        var principal = MakePrincipal(HumanCapability.ApproveTask); // has a different capability

        var ex = Assert.Throws<HumanAuthorizationException>(() =>
            service.RequireHuman(principal, HumanCapability.ActivateAgent, "Activate", "resource-1"));

        Assert.Equal(AuthorityViolationKind.CapabilityMissing, ex.ViolationKind);
        Assert.Equal(ActorType.Human, ex.ActorType);
    }

    [Fact]
    public void RequireHuman_GuidOverload_NullPrincipal_ThrowsHumanAuthorizationException()
    {
        var service = BuildService();

        var ex = Assert.Throws<HumanAuthorizationException>(() =>
            service.RequireHuman(null, HumanCapability.SuspendAgent, "Suspend", Guid.NewGuid()));

        Assert.Equal(AuthorityViolationKind.UnauthorizedApprovalAttempt, ex.ViolationKind);
    }

    [Fact]
    public void RequireHuman_GuidOverload_PrincipalWithCapability_DoesNotThrow()
    {
        var service = BuildService();
        var principal = MakePrincipal(HumanCapability.SuspendAgent);

        var ex = Record.Exception(() =>
            service.RequireHuman(principal, HumanCapability.SuspendAgent, "Suspend", Guid.NewGuid()));

        Assert.Null(ex);
    }
}
