using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Proves that when the primary <see cref="IAuditRepository"/> append fails, the security event
/// is written to the durable <see cref="ISecurityAuditDeadLetterRepository"/> instead.
/// Denial is enforced synchronously; durability is confirmed by awaiting the internal
/// <c>PersistSecurityEventAsync</c> deterministically.
/// </summary>
public class SecurityDeadLetterFallbackTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;

    public SecurityDeadLetterFallbackTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RebelgentDbContext>().UseSqlite(_connection).Options;
        _db = new RebelgentDbContext(options);
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class ThrowingAuditRepository : IAuditRepository
    {
        public Task<AuditEvent> AppendAsync(AuditEvent e, CancellationToken ct = default)
            => throw new InvalidOperationException("primary ledger down");
        public Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default) => Task.FromResult<AuditEvent?>(null);
        public Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    private IServiceScopeFactory BuildScopeFactoryWithThrowingPrimary(EfSecurityAuditDeadLetterRepository deadLetterRepo)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAuditRepository, ThrowingAuditRepository>();
        services.AddScoped<ISecurityAuditDeadLetterRepository>(_ => deadLetterRepo);
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddLogging();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task PrimaryAuditFails_EventPersistedToDeadLetter()
    {
        var deadLetterRepo = new EfSecurityAuditDeadLetterRepository(_db);
        var scopeFactory = BuildScopeFactoryWithThrowingPrimary(deadLetterRepo);
        var service = new HumanAuthorizationService(scopeFactory, NullLogger<HumanAuthorizationService>.Instance);

        // Call the internal deterministic path directly so we can await completion.
        await service.PersistSecurityEventAsync(
            AuditEventType.UnauthorizedApprovalAttempt,
            ActorType.Agent,
            "unknown",
            "res-42",
            "Activate",
            "details");

        var entries = await deadLetterRepo.GetAllAsync();
        Assert.Single(entries);
        Assert.Equal(AuditEventType.UnauthorizedApprovalAttempt, entries[0].EventType);
        Assert.Equal("res-42", entries[0].ResourceId);
        Assert.Contains("primary ledger down", entries[0].PrimaryAuditError);
    }

    [Fact]
    public async Task Denial_StillEnforced_WhenPrimaryAuditFails()
    {
        var deadLetterRepo = new EfSecurityAuditDeadLetterRepository(_db);
        var scopeFactory = BuildScopeFactoryWithThrowingPrimary(deadLetterRepo);
        var service = new HumanAuthorizationService(scopeFactory, NullLogger<HumanAuthorizationService>.Instance);

        // Denial must throw synchronously, even though the background audit will fail.
        Assert.Throws<HumanAuthorizationException>(() =>
            service.RequireHuman(null, HumanCapability.ActivateAgent, "Activate", "res-1"));
    }

    [Fact]
    public async Task MultipleDenials_EachProducesADeadLetterEntry()
    {
        var deadLetterRepo = new EfSecurityAuditDeadLetterRepository(_db);
        var scopeFactory = BuildScopeFactoryWithThrowingPrimary(deadLetterRepo);
        var service = new HumanAuthorizationService(scopeFactory, NullLogger<HumanAuthorizationService>.Instance);

        await service.PersistSecurityEventAsync(
            AuditEventType.UnauthorizedApprovalAttempt,
            ActorType.Agent, "unknown", "r-1", "Activate", "d1");
        await service.PersistSecurityEventAsync(
            AuditEventType.UnauthorizedApprovalAttempt,
            ActorType.Agent, "unknown", "r-2", "Suspend", "d2");

        var entries = await deadLetterRepo.GetAllAsync();
        Assert.Equal(2, entries.Count);
    }
}
