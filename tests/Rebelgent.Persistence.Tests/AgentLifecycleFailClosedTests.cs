using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Proves that privileged agent-lifecycle transitions (Activate/Suspend/Retire) are atomic
/// with their audit event. A failure to persist the audit event MUST roll back the state
/// transition. Uses real SQLite so real transactions and triggers are exercised.
/// </summary>
public class AgentLifecycleFailClosedTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfAgentDefinitionRepository _agentRepo;
    private readonly EfAgentVersionRepository _versionRepo;
    private readonly EfAuditRepository _realAuditRepo;
    private readonly EfUnitOfWork _unitOfWork;

    public AgentLifecycleFailClosedTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new RebelgentDbContext(options);
        _db.Database.Migrate();

        _agentRepo = new EfAgentDefinitionRepository(_db);
        _versionRepo = new EfAgentVersionRepository(_db);
        _realAuditRepo = new EfAuditRepository(_db);
        _unitOfWork = new EfUnitOfWork(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class FlakyAuditRepository : IAuditRepository
    {
        private readonly IAuditRepository _inner;
        public bool NextAppendThrows { get; set; }
        public FlakyAuditRepository(IAuditRepository inner) { _inner = inner; }

        public Task<AuditEvent> AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            if (NextAppendThrows)
            {
                NextAppendThrows = false;
                throw new InvalidOperationException("simulated audit persistence failure");
            }
            return _inner.AppendAsync(auditEvent, ct);
        }

        public Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default) => _inner.GetLatestAsync(ct);
        public Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default) => _inner.GetAllOrderedAsync(ct);
        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default) => _inner.GetRecentAsync(count, ct);
    }

    private (AgentLifecycleService service, FlakyAuditRepository flakyRepo) BuildService()
    {
        var flakyRepo = new FlakyAuditRepository(_realAuditRepo);
        var auditService = new AuditService(flakyRepo, NullLogger<AuditService>.Instance);
        var scopeFactory = TestSecurityScopeFactory.Build();
        var authService = new HumanAuthorizationService(scopeFactory, NullLogger<HumanAuthorizationService>.Instance);
        var service = new AgentLifecycleService(authService, _agentRepo, _versionRepo, auditService, _unitOfWork);
        return (service, flakyRepo);
    }

    private static HumanPrincipal FullyCapableHuman() =>
        new(Guid.NewGuid(), "Test", "user-1",
            Enum.GetValues<HumanCapability>().ToHashSet(),
            DateTimeOffset.UtcNow);

    private async Task<Guid> SeedAgentInStatusAsync(AgentLifecycleStatus status)
    {
        var agent = new AgentDefinition("Alpha", AgentRole.BackendDeveloper, "Purpose", "Description", Guid.NewGuid());
        // Force status via reflection on the reconstitute path.
        var seeded = AgentDefinitionInternalHelpers.WithStatus(agent, status);
        await _agentRepo.AddAsync(seeded);
        return seeded.Id;
    }

    [Fact]
    public async Task Activate_AuditPersistenceFails_RollsBackAgentStatus()
    {
        var (service, flaky) = BuildService();
        var agentId = await SeedAgentInStatusAsync(AgentLifecycleStatus.AwaitingApproval);

        flaky.NextAppendThrows = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivateAsync(agentId, FullyCapableHuman()));

        // Detach any cached tracker so we read from the database.
        _db.ChangeTracker.Clear();
        var reloaded = await _agentRepo.GetByIdAsync(agentId);

        Assert.NotNull(reloaded);
        Assert.Equal(AgentLifecycleStatus.AwaitingApproval, reloaded.Status);
        Assert.Null(reloaded.ActivatedAt);
    }

    [Fact]
    public async Task Suspend_AuditPersistenceFails_RollsBackAgentStatus()
    {
        var (service, flaky) = BuildService();
        var agentId = await SeedAgentInStatusAsync(AgentLifecycleStatus.Active);

        flaky.NextAppendThrows = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SuspendAsync(agentId, FullyCapableHuman()));

        _db.ChangeTracker.Clear();
        var reloaded = await _agentRepo.GetByIdAsync(agentId);

        Assert.NotNull(reloaded);
        Assert.Equal(AgentLifecycleStatus.Active, reloaded.Status);
        Assert.Null(reloaded.SuspendedAt);
    }

    [Fact]
    public async Task Retire_AuditPersistenceFails_RollsBackAgentStatus()
    {
        var (service, flaky) = BuildService();
        var agentId = await SeedAgentInStatusAsync(AgentLifecycleStatus.Active);

        flaky.NextAppendThrows = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RetireAsync(agentId, FullyCapableHuman()));

        _db.ChangeTracker.Clear();
        var reloaded = await _agentRepo.GetByIdAsync(agentId);

        Assert.NotNull(reloaded);
        Assert.Equal(AgentLifecycleStatus.Active, reloaded.Status);
        Assert.Null(reloaded.RetiredAt);
    }

    [Fact]
    public async Task Activate_Succeeds_WhenAuditPersists_AgentAndAuditBothCommitted()
    {
        var (service, _) = BuildService();
        var agentId = await SeedAgentInStatusAsync(AgentLifecycleStatus.AwaitingApproval);

        var result = await service.ActivateAsync(agentId, FullyCapableHuman());

        Assert.Equal(AgentLifecycleStatus.Active, result.Status);
        Assert.NotNull(result.ActivatedAt);

        _db.ChangeTracker.Clear();
        var events = await _realAuditRepo.GetAllOrderedAsync();
        Assert.Contains(events, e =>
            e.EventType == AuditEventType.AgentActivated
            && e.ResourceId == agentId.ToString());
    }

    [Fact]
    public async Task Activate_AuditFails_NoAuditEventPersistedForFailedTransition()
    {
        var (service, flaky) = BuildService();
        var agentId = await SeedAgentInStatusAsync(AgentLifecycleStatus.AwaitingApproval);

        flaky.NextAppendThrows = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivateAsync(agentId, FullyCapableHuman()));

        _db.ChangeTracker.Clear();
        var events = await _realAuditRepo.GetAllOrderedAsync();
        Assert.DoesNotContain(events, e =>
            e.EventType == AuditEventType.AgentActivated
            && e.ResourceId == agentId.ToString());
    }
}

/// <summary>
/// Test helper that reconstitutes an <see cref="AgentDefinition"/> in a specific lifecycle
/// status without going through the public transitions (which would require an authorized
/// principal and side-effects we do not want in the arrange phase).
/// </summary>
internal static class AgentDefinitionInternalHelpers
{
    public static AgentDefinition WithStatus(AgentDefinition source, AgentLifecycleStatus status)
    {
        var method = typeof(AgentDefinition).GetMethod(
            "Reconstitute",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Reconstitute method not found on AgentDefinition.");
        return (AgentDefinition)method.Invoke(null,
        [
            source.Id, source.Name, source.Role, source.Purpose, source.Description,
            status, source.CurrentVersionId, source.CreatedAt, source.CreatedByHumanId,
            source.ActivatedAt, source.SuspendedAt, source.RetiredAt
        ])!;
    }
}
