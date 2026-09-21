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
/// Proves the compatibility bootstrap imports pre-M10 built-in agents into the governed
/// registry, is idempotent, records audit events, and cannot activate arbitrary agents.
/// </summary>
public class BuiltInAgentBootstrapperTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfAgentDefinitionRepository _agentRepo;
    private readonly EfAgentVersionRepository _versionRepo;
    private readonly EfAuditRepository _auditRepo;
    private readonly EfUnitOfWork _unitOfWork;
    private readonly BuiltInAgentBootstrapper _bootstrapper;
    private readonly AuditService _auditService;

    public BuiltInAgentBootstrapperTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RebelgentDbContext>().UseSqlite(_connection).Options;
        _db = new RebelgentDbContext(options);
        _db.Database.Migrate();

        _agentRepo = new EfAgentDefinitionRepository(_db);
        _versionRepo = new EfAgentVersionRepository(_db);
        _auditRepo = new EfAuditRepository(_db);
        _unitOfWork = new EfUnitOfWork(_db);
        _auditService = new AuditService(_auditRepo, NullLogger<AuditService>.Instance);

        _bootstrapper = new BuiltInAgentBootstrapper(
            _agentRepo, _versionRepo, _auditService, _unitOfWork,
            NullLogger<BuiltInAgentBootstrapper>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task EnsureBootstrapped_FirstRun_ImportsAllBuiltIns()
    {
        var result = await _bootstrapper.EnsureBootstrappedAsync();

        Assert.Equal(6, result.TotalBuiltIns);
        Assert.Equal(6, result.Imported);
        Assert.Equal(0, result.AlreadyPresent);

        _db.ChangeTracker.Clear();
        var all = await _agentRepo.GetAllAsync();
        Assert.Equal(6, all.Count);
        Assert.Contains(all, a => a.Name == "Developer");
        Assert.Contains(all, a => a.Name == "QA Engineer");
        Assert.Contains(all, a => a.Name == "Code Reviewer");
        Assert.Contains(all, a => a.Name == "Release Manager");
        Assert.Contains(all, a => a.Name == "Improvement Analyst");
        Assert.Contains(all, a => a.Name == "Agent Evolution Manager");
    }

    [Fact]
    public async Task EnsureBootstrapped_ImportsAsActive()
    {
        await _bootstrapper.EnsureBootstrappedAsync();

        _db.ChangeTracker.Clear();
        var all = await _agentRepo.GetAllAsync();
        Assert.All(all, a => Assert.Equal(AgentLifecycleStatus.Active, a.Status));
        Assert.All(all, a => Assert.NotNull(a.ActivatedAt));
    }

    [Fact]
    public async Task EnsureBootstrapped_CreatesInitialImmutableVersionPerAgent()
    {
        await _bootstrapper.EnsureBootstrappedAsync();

        _db.ChangeTracker.Clear();
        var all = await _agentRepo.GetAllAsync();
        foreach (var agent in all)
        {
            Assert.NotNull(agent.CurrentVersionId);
            var versions = await _versionRepo.GetForAgentAsync(agent.Id);
            Assert.Single(versions);
            Assert.Equal(AgentVersionStatus.Active, versions[0].Status);
            Assert.Equal(agent.CurrentVersionId, versions[0].Id);
            Assert.Equal("1.0.0", versions[0].Version);
        }
    }

    [Fact]
    public async Task EnsureBootstrapped_SecondRun_IsIdempotent_NoNewImports()
    {
        await _bootstrapper.EnsureBootstrappedAsync();
        _db.ChangeTracker.Clear();
        var second = await _bootstrapper.EnsureBootstrappedAsync();

        Assert.Equal(6, second.TotalBuiltIns);
        Assert.Equal(0, second.Imported);
        Assert.Equal(6, second.AlreadyPresent);

        _db.ChangeTracker.Clear();
        var all = await _agentRepo.GetAllAsync();
        Assert.Equal(6, all.Count);
    }

    [Fact]
    public async Task EnsureBootstrapped_SecondRun_DoesNotCreateDuplicateVersions()
    {
        await _bootstrapper.EnsureBootstrappedAsync();
        _db.ChangeTracker.Clear();
        await _bootstrapper.EnsureBootstrappedAsync();

        _db.ChangeTracker.Clear();
        var all = await _agentRepo.GetAllAsync();
        foreach (var agent in all)
        {
            var versions = await _versionRepo.GetForAgentAsync(agent.Id);
            Assert.Single(versions);
        }
    }

    [Fact]
    public async Task EnsureBootstrapped_EmitsAuditEventPerImportedBuiltIn()
    {
        await _bootstrapper.EnsureBootstrappedAsync();

        _db.ChangeTracker.Clear();
        var events = await _auditRepo.GetAllOrderedAsync();
        var importEvents = events.Where(e => e.EventType == AuditEventType.BuiltInAgentImported).ToList();
        Assert.Equal(6, importEvents.Count);
        Assert.All(importEvents, e => Assert.Equal(ActorType.System, e.ActorType));
        Assert.All(importEvents, e => Assert.Equal("BuiltInAgentBootstrapper", e.ActorId));
    }

    [Fact]
    public async Task EnsureBootstrapped_SecondRun_DoesNotEmitDuplicateAuditEvents()
    {
        await _bootstrapper.EnsureBootstrappedAsync();
        _db.ChangeTracker.Clear();
        await _bootstrapper.EnsureBootstrappedAsync();

        _db.ChangeTracker.Clear();
        var events = await _auditRepo.GetAllOrderedAsync();
        var importEvents = events.Where(e => e.EventType == AuditEventType.BuiltInAgentImported).ToList();
        Assert.Equal(6, importEvents.Count);
    }

    [Fact]
    public async Task EnsureBootstrapped_AuditChainRemainsValidAfterBootstrap()
    {
        await _bootstrapper.EnsureBootstrappedAsync();

        _db.ChangeTracker.Clear();
        var verifier = new AuditLedgerVerifier(_auditRepo);
        var verify = await verifier.VerifyAsync();
        Assert.True(verify.IsValid, verify.FailureReason);
        Assert.Equal(6, verify.VerifiedEventCount);
    }

    [Fact]
    public async Task EnsureBootstrapped_AgentIdsAreDeterministic()
    {
        await _bootstrapper.EnsureBootstrappedAsync();
        _db.ChangeTracker.Clear();
        var firstRunAgents = (await _agentRepo.GetAllAsync()).OrderBy(a => a.Name).Select(a => a.Id).ToList();

        // Simulate restart with a fresh bootstrapper instance against the same DB.
        var freshBootstrapper = new BuiltInAgentBootstrapper(
            _agentRepo, _versionRepo, _auditService, _unitOfWork,
            NullLogger<BuiltInAgentBootstrapper>.Instance);
        await freshBootstrapper.EnsureBootstrappedAsync();

        _db.ChangeTracker.Clear();
        var secondRunAgents = (await _agentRepo.GetAllAsync()).OrderBy(a => a.Name).Select(a => a.Id).ToList();
        Assert.Equal(firstRunAgents, secondRunAgents);
    }

    [Fact]
    public async Task Bootstrap_CannotBeUsedToActivateArbitraryAgent()
    {
        // Only the fixed compile-time BuiltIns list is ever imported. There is no public API
        // that lets a caller (agent or human) pass in a name/role and get an Active row.
        var bootstrapperType = typeof(BuiltInAgentBootstrapper);
        var publicMethods = bootstrapperType
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.DoesNotContain(publicMethods, m => m.Name.Contains("Activate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicMethods, m => m.Name.Contains("Import", StringComparison.OrdinalIgnoreCase)
            && m.Name != nameof(BuiltInAgentBootstrapper.EnsureBootstrappedAsync));
        Assert.DoesNotContain(publicMethods, m => m.GetParameters().Any(p =>
            p.ParameterType == typeof(AgentDefinition)
            || p.ParameterType == typeof(string) && m.Name.Contains("Register", StringComparison.OrdinalIgnoreCase)));

        // The interface itself exposes ONE method — EnsureBootstrappedAsync.
        var interfaceMethods = typeof(IBuiltInAgentBootstrapper)
            .GetMethods()
            .Select(m => m.Name)
            .ToList();
        Assert.Single(interfaceMethods);
        Assert.Equal(nameof(IBuiltInAgentBootstrapper.EnsureBootstrappedAsync), interfaceMethods[0]);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Bootstrap_ImportedAgentUsesSystemBootstrapActorId_NotAnyRealHuman()
    {
        await _bootstrapper.EnsureBootstrappedAsync();

        _db.ChangeTracker.Clear();
        var all = await _agentRepo.GetAllAsync();
        Assert.All(all, a => Assert.Equal(BuiltInAgentBootstrapper.SystemBootstrapActorId, a.CreatedByHumanId));
    }
}
