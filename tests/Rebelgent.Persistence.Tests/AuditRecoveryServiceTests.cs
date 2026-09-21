using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Proves that <see cref="AuditRecoveryService"/> repairs the audit log without replaying the
/// underlying privileged action, preserves the original dead-letter row, and is idempotent.
/// </summary>
public class AuditRecoveryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfSecurityAuditDeadLetterRepository _deadLetterRepo;
    private readonly EfAuditRepository _auditRepo;
    private readonly EfAgentDefinitionRepository _agentRepo;
    private readonly EfUnitOfWork _unitOfWork;
    private readonly AuditRecoveryService _service;

    public AuditRecoveryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RebelgentDbContext>().UseSqlite(_connection).Options;
        _db = new RebelgentDbContext(options);
        _db.Database.Migrate();

        _deadLetterRepo = new EfSecurityAuditDeadLetterRepository(_db);
        _auditRepo = new EfAuditRepository(_db);
        _agentRepo = new EfAgentDefinitionRepository(_db);
        _unitOfWork = new EfUnitOfWork(_db);

        var scopeFactory = TestSecurityScopeFactory.Build();
        var authService = new HumanAuthorizationService(scopeFactory, NullLogger<HumanAuthorizationService>.Instance);
        _service = new AuditRecoveryService(_deadLetterRepo, _auditRepo, authService, _unitOfWork,
            NullLogger<AuditRecoveryService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static HumanPrincipal FullyCapableHuman() =>
        new(Guid.NewGuid(), "Test", "user-1",
            Enum.GetValues<HumanCapability>().ToHashSet(),
            DateTimeOffset.UtcNow);

    private async Task<SecurityAuditDeadLetter> SeedDeadLetterAsync(string eventType = AuditEventType.UnauthorizedApprovalAttempt)
    {
        var entry = new SecurityAuditDeadLetter(
            new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero),
            eventType,
            ActorType.Agent,
            "actor-1",
            "Security",
            "res-42",
            "Activate",
            "{\"details\":\"unauthorized\"}",
            "primary ledger down");
        return await _deadLetterRepo.AppendAsync(entry);
    }

    [Fact]
    public async Task RecoverAsync_UnknownId_ReturnsNotFound()
    {
        var result = await _service.RecoverAsync(Guid.NewGuid(), FullyCapableHuman());
        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoverAsync_CreatesCanonicalAuditEventPreservingOriginalTimestamp()
    {
        var entry = await SeedDeadLetterAsync();

        var result = await _service.RecoverAsync(entry.Id, FullyCapableHuman());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.RecoveredAuditEventId);

        _db.ChangeTracker.Clear();
        var events = await _auditRepo.GetAllOrderedAsync();
        Assert.Single(events);
        Assert.Equal(entry.TimestampUtc, events[0].TimestampUtc);
        Assert.Equal(entry.EventType, events[0].EventType);
        Assert.Equal(entry.ActorId, events[0].ActorId);
        Assert.Equal(entry.ResourceId, events[0].ResourceId);
        Assert.Equal(entry.PayloadJson, events[0].PayloadJson);
    }

    [Fact]
    public async Task RecoverAsync_PreservesOriginalDeadLetterRow()
    {
        var entry = await SeedDeadLetterAsync();

        await _service.RecoverAsync(entry.Id, FullyCapableHuman());

        _db.ChangeTracker.Clear();
        var reloaded = await _deadLetterRepo.GetByIdAsync(entry.Id);
        Assert.NotNull(reloaded);
        // All original fields intact — never updated.
        Assert.Equal(entry.EventType, reloaded.EventType);
        Assert.Equal(entry.ActorId, reloaded.ActorId);
        Assert.Equal(entry.PayloadJson, reloaded.PayloadJson);
    }

    [Fact]
    public async Task RecoverAsync_AppendsRecoveryLinkageRow()
    {
        var entry = await SeedDeadLetterAsync();

        var result = await _service.RecoverAsync(entry.Id, FullyCapableHuman());

        _db.ChangeTracker.Clear();
        var recoveries = await _deadLetterRepo.GetRecoveriesAsync(entry.Id);
        Assert.Single(recoveries);
        Assert.Equal(result.RecoveredAuditEventId, recoveries[0].RecoveredAuditEventId);
        Assert.Equal(entry.Id, recoveries[0].DeadLetterId);
    }

    [Fact]
    public async Task RecoverAsync_IsIdempotent_SecondCallReturnsAlreadyResolved()
    {
        var entry = await SeedDeadLetterAsync();

        var first = await _service.RecoverAsync(entry.Id, FullyCapableHuman());
        var second = await _service.RecoverAsync(entry.Id, FullyCapableHuman());

        Assert.False(first.WasAlreadyResolved);
        Assert.True(second.WasAlreadyResolved);
        Assert.Equal(first.RecoveredAuditEventId, second.RecoveredAuditEventId);

        _db.ChangeTracker.Clear();
        var events = await _auditRepo.GetAllOrderedAsync();
        Assert.Single(events); // only one canonical audit event created
        var recoveries = await _deadLetterRepo.GetRecoveriesAsync(entry.Id);
        Assert.Single(recoveries); // only one linkage row
    }

    [Fact]
    public async Task RecoverAsync_DoesNotReplayPrivilegedAction_AgentStateUnchanged()
    {
        // Seed an agent — recovery of a dead-letter for an "Activate" action must NOT actually
        // activate the agent. Recovery only repairs audit logs.
        var agent = new AgentDefinition("Alpha", AgentRole.BackendDeveloper, "Purpose", "Description", Guid.NewGuid());
        var seeded = AgentDefinitionInternalHelpers.WithStatus(agent, AgentLifecycleStatus.AwaitingApproval);
        await _agentRepo.AddAsync(seeded);

        var entry = new SecurityAuditDeadLetter(
            DateTimeOffset.UtcNow,
            AuditEventType.UnauthorizedApprovalAttempt,
            ActorType.Agent, "actor-1", "AgentDefinition", seeded.Id.ToString(),
            "Activate", "{}", "primary down");
        await _deadLetterRepo.AppendAsync(entry);

        await _service.RecoverAsync(entry.Id, FullyCapableHuman());

        _db.ChangeTracker.Clear();
        var reloaded = await _agentRepo.GetByIdAsync(seeded.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(AgentLifecycleStatus.AwaitingApproval, reloaded.Status);
        Assert.Null(reloaded.ActivatedAt);
    }

    [Fact]
    public async Task RecoverAsync_UnresolvedCountDecrementsAfterRecovery()
    {
        var a = await SeedDeadLetterAsync();
        var b = await SeedDeadLetterAsync();

        Assert.Equal(2, await _deadLetterRepo.CountUnresolvedAsync());

        await _service.RecoverAsync(a.Id, FullyCapableHuman());

        _db.ChangeTracker.Clear();
        Assert.Equal(1, await _deadLetterRepo.CountUnresolvedAsync());
    }

    [Fact]
    public async Task RecoverAsync_UnauthorizedHuman_ThrowsBeforeAnyStateChange()
    {
        var entry = await SeedDeadLetterAsync();

        await Assert.ThrowsAsync<HumanAuthorizationException>(() =>
            _service.RecoverAsync(entry.Id, null!));

        _db.ChangeTracker.Clear();
        var events = await _auditRepo.GetAllOrderedAsync();
        Assert.Empty(events);
        var recoveries = await _deadLetterRepo.GetRecoveriesAsync(entry.Id);
        Assert.Empty(recoveries);
    }

    [Fact]
    public async Task RecoverAllUnresolvedAsync_RecoversEverything_ReportsCounts()
    {
        await SeedDeadLetterAsync();
        await SeedDeadLetterAsync();
        await SeedDeadLetterAsync();

        var batch = await _service.RecoverAllUnresolvedAsync(FullyCapableHuman());

        Assert.Equal(3, batch.Attempted);
        Assert.Equal(3, batch.RecoveredNow);
        Assert.Equal(0, batch.AlreadyResolved);
        Assert.Equal(0, batch.Failed);

        _db.ChangeTracker.Clear();
        Assert.Equal(0, await _deadLetterRepo.CountUnresolvedAsync());
    }

    [Fact]
    public async Task Repository_ExposesNoDeleteOrClearMethod()
    {
        var type = typeof(Rebelgent.Core.Repositories.ISecurityAuditDeadLetterRepository);
        var members = type.GetMembers();
        Assert.DoesNotContain(members, m =>
            m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || m.Name.Contains("Clear", StringComparison.OrdinalIgnoreCase)
            || m.Name.Contains("Truncate", StringComparison.OrdinalIgnoreCase)
            || m.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }
}
