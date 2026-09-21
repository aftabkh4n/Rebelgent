using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;

namespace Rebelgent.Core.Tests.Authority;

/// <summary>
/// Builds an <see cref="IServiceScopeFactory"/> containing minimal in-memory implementations of
/// the audit repository and the security dead-letter repository. Used by tests that need to
/// construct a <see cref="HumanAuthorizationService"/> without pulling in the full DI graph.
/// </summary>
internal static class SilentAuditScopeFactoryBuilder
{
    public static IServiceScopeFactory Build()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAuditRepository, InMemoryAuditRepository>();
        services.AddScoped<ISecurityAuditDeadLetterRepository, InMemoryDeadLetterRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddLogging();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    internal sealed class InMemoryAuditRepository : IAuditRepository
    {
        private readonly List<AuditEvent> _events = [];
        public Task<AuditEvent> AppendAsync(AuditEvent e, CancellationToken ct = default) { _events.Add(e); return Task.FromResult(e); }
        public Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default) => Task.FromResult(_events.Count == 0 ? null : _events[^1]);
        public Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEvent>>(_events.OrderBy(e => e.SequenceNumber).ToList());
        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEvent>>(_events.OrderByDescending(e => e.SequenceNumber).Take(count).ToList());
    }

    internal sealed class InMemoryDeadLetterRepository : ISecurityAuditDeadLetterRepository
    {
        public List<SecurityAuditDeadLetter> Entries { get; } = [];
        public List<SecurityAuditDeadLetterRecovery> Recoveries { get; } = [];
        public Task<SecurityAuditDeadLetter> AppendAsync(SecurityAuditDeadLetter e, CancellationToken ct = default) { Entries.Add(e); return Task.FromResult(e); }
        public Task<SecurityAuditDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));
        public Task<IReadOnlyList<SecurityAuditDeadLetter>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityAuditDeadLetter>>(Entries.ToList());
        public Task<IReadOnlyList<SecurityAuditDeadLetter>> GetUnresolvedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityAuditDeadLetter>>(Entries.Where(e => Recoveries.All(r => r.DeadLetterId != e.Id)).ToList());
        public Task<int> CountUnresolvedAsync(CancellationToken ct = default) => Task.FromResult(Entries.Count(e => Recoveries.All(r => r.DeadLetterId != e.Id)));
        public Task<SecurityAuditDeadLetterRecovery> AppendRecoveryAsync(SecurityAuditDeadLetterRecovery r, CancellationToken ct = default) { Recoveries.Add(r); return Task.FromResult(r); }
        public Task<IReadOnlyList<SecurityAuditDeadLetterRecovery>> GetRecoveriesAsync(Guid deadLetterId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityAuditDeadLetterRecovery>>(Recoveries.Where(r => r.DeadLetterId == deadLetterId).ToList());
    }
}
