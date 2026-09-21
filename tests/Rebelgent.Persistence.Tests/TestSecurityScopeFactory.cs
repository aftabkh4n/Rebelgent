using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Builds an <see cref="IServiceScopeFactory"/> with no-op in-memory audit + dead-letter
/// repositories for tests that don't care about background security-event durability but need
/// <see cref="HumanAuthorizationService"/> to be constructible.
/// </summary>
internal static class TestSecurityScopeFactory
{
    public static IServiceScopeFactory Build()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAuditRepository, NoOpAuditRepository>();
        services.AddScoped<ISecurityAuditDeadLetterRepository, NoOpDeadLetterRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddLogging();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class NoOpAuditRepository : IAuditRepository
    {
        public Task<AuditEvent> AppendAsync(AuditEvent e, CancellationToken ct = default) => Task.FromResult(e);
        public Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default) => Task.FromResult<AuditEvent?>(null);
        public Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    private sealed class NoOpDeadLetterRepository : ISecurityAuditDeadLetterRepository
    {
        public Task<SecurityAuditDeadLetter> AppendAsync(SecurityAuditDeadLetter e, CancellationToken ct = default) => Task.FromResult(e);
        public Task<SecurityAuditDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<SecurityAuditDeadLetter?>(null);
        public Task<IReadOnlyList<SecurityAuditDeadLetter>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityAuditDeadLetter>>([]);
        public Task<IReadOnlyList<SecurityAuditDeadLetter>> GetUnresolvedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityAuditDeadLetter>>([]);
        public Task<int> CountUnresolvedAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<SecurityAuditDeadLetterRecovery> AppendRecoveryAsync(SecurityAuditDeadLetterRecovery r, CancellationToken ct = default) => Task.FromResult(r);
        public Task<IReadOnlyList<SecurityAuditDeadLetterRecovery>> GetRecoveriesAsync(Guid deadLetterId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityAuditDeadLetterRecovery>>([]);
    }
}
