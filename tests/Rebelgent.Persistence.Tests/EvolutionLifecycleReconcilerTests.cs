using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rebelgent.ClaudeCode.Evolution;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Proves the evolution lifecycle reconciler derives Implemented state and repairs
/// persisted-only TargetProjectId gaps strictly from governed evidence, is idempotent,
/// and cannot grant human authority or activate arbitrary agents.
/// </summary>
public class EvolutionLifecycleReconcilerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfAgentEvolutionProposalRepository _proposalRepo;
    private readonly AgentTaskRepository _taskRepo;
    private readonly EfAuditRepository _auditRepo;
    private readonly EfUnitOfWork _unitOfWork;
    private readonly AuditService _auditService;

    private const string ConfiguredProjectId = "sandbox";

    public EvolutionLifecycleReconcilerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RebelgentDbContext>().UseSqlite(_connection).Options;
        _db = new RebelgentDbContext(options);
        _db.Database.Migrate();

        _proposalRepo = new EfAgentEvolutionProposalRepository(_db);
        _taskRepo = new AgentTaskRepository(_db);
        _auditRepo = new EfAuditRepository(_db);
        _unitOfWork = new EfUnitOfWork(_db);
        _auditService = new AuditService(_auditRepo, NullLogger<AuditService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class TestProjectRegistry : IProjectRegistry
    {
        private readonly string _projectId;
        public TestProjectRegistry(string projectId) => _projectId = projectId;

        public IReadOnlyCollection<ProjectDefinition> GetAll() =>
            [new ProjectDefinition { Id = _projectId, Name = _projectId, RepositoryPath = "D:\\Projects\\" + _projectId }];

        public ProjectDefinition? Find(string projectId) =>
            string.Equals(projectId, _projectId, StringComparison.OrdinalIgnoreCase)
                ? GetAll().Single()
                : null;
    }

    private sealed class FlakyAuditRepository : IAuditRepository
    {
        private readonly IAuditRepository _inner;
        public string? FailOnEventType { get; set; }

        public FlakyAuditRepository(IAuditRepository inner) { _inner = inner; }

        public Task<AuditEvent> AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            if (FailOnEventType is not null && auditEvent.EventType == FailOnEventType)
                throw new InvalidOperationException("simulated audit persistence failure");
            return _inner.AppendAsync(auditEvent, ct);
        }

        public Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default) => _inner.GetLatestAsync(ct);
        public Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default) => _inner.GetAllOrderedAsync(ct);
        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default) => _inner.GetRecentAsync(count, ct);
    }

    private EvolutionLifecycleReconciler BuildReconciler(
        string projectId = ConfiguredProjectId,
        IAuditService? auditServiceOverride = null,
        IProjectRegistry? registryOverride = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AgentEvolutionOptions { ProjectId = projectId });
        return new EvolutionLifecycleReconciler(
            _proposalRepo, _taskRepo,
            auditServiceOverride ?? _auditService,
            _unitOfWork,
            registryOverride ?? new TestProjectRegistry(ConfiguredProjectId),
            options,
            NullLogger<EvolutionLifecycleReconciler>.Instance);
    }

    private async Task<Guid> SeedApprovedProposalWithTaskAsync(
        AgentTaskStatus taskStatus,
        string? mergeCommitSha,
        int? pullRequestNumber,
        string targetProjectId = ConfiguredProjectId)
    {
        var task = new AgentTask(ConfiguredProjectId, "impl", "body", AgentRole.BackendDeveloper);
        if (taskStatus != AgentTaskStatus.Created)
            task.GetType().GetMethod("SetStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(task, new object[] { taskStatus });
        if (pullRequestNumber is int pr)
            task.SetPullRequestInfo(pr, $"https://example/pr/{pr}");
        if (!string.IsNullOrWhiteSpace(mergeCommitSha))
            task.SetMergeInfo(mergeCommitSha!, "squash");
        await _taskRepo.AddAsync(task);

        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(),
            AgentEvolutionProposalType.ModifyAgent,
            targetProjectId,
            null, null, null,
            "Purpose", "Evidence", "Change", null, null,
            RiskLevel.Low, "eval",
            AgentEvolutionProposalStatus.Approved,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddHours(-2),
            task.Id);
        await _proposalRepo.AddAsync(proposal);
        return proposal.Id;
    }

    // ==== TargetProjectId repair ====

    [Fact]
    public async Task Reconcile_ProposalWithBlankTargetProjectId_RepairsFromConfiguredProject()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Created, null, null, targetProjectId: "");

        var reconciler = BuildReconciler();
        var result = await reconciler.ReconcileAsync();

        Assert.Equal(1, result.TargetProjectsRepaired);
        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(reloaded);
        Assert.Equal(ConfiguredProjectId, reloaded!.TargetProjectId);
    }

    [Fact]
    public async Task Reconcile_RepairEmitsExactlyOneEvolutionTargetProjectRepairedEvent()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Created, null, null, targetProjectId: "");

        var reconciler = BuildReconciler();
        await reconciler.ReconcileAsync();
        await reconciler.ReconcileAsync();

        _db.ChangeTracker.Clear();
        var events = await _auditRepo.GetAllOrderedAsync();
        var repairs = events.Where(e =>
            e.EventType == AuditEventType.EvolutionTargetProjectRepaired
            && e.ResourceId == proposalId.ToString()).ToList();
        Assert.Single(repairs);
    }

    [Fact]
    public async Task Reconcile_RepairPersistsAcrossFreshDbContext()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Created, null, null, targetProjectId: "");

        var reconciler = BuildReconciler();
        await reconciler.ReconcileAsync();

        var freshOptions = new DbContextOptionsBuilder<RebelgentDbContext>().UseSqlite(_connection).Options;
        using var freshDb = new RebelgentDbContext(freshOptions);
        var freshRepo = new EfAgentEvolutionProposalRepository(freshDb);
        var reloaded = await freshRepo.GetByIdAsync(proposalId);

        Assert.NotNull(reloaded);
        Assert.Equal(ConfiguredProjectId, reloaded!.TargetProjectId);
    }

    [Fact]
    public async Task Reconcile_ExistingTargetProjectIsNeverOverwritten()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Created, null, null, targetProjectId: "existing");
        var reconciler = BuildReconciler(projectId: "sandbox", registryOverride: new TestProjectRegistry("existing"));

        var result = await reconciler.ReconcileAsync();

        Assert.Equal(0, result.TargetProjectsRepaired);
        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.Equal("existing", reloaded!.TargetProjectId);
    }

    [Fact]
    public async Task Reconcile_DoesNotDuplicateLegacyBackfillAuditEvent()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Created, null, null, targetProjectId: "");
        // Simulate the prior LegacyEvolutionTargetBackfilled event that lives in the ledger for
        // proposal b18206c5 in production.
        await _auditService.RecordAsync(
            AuditEventType.LegacyEvolutionTargetBackfilled,
            Core.Authority.ActorType.System, "AgentEvolutionOrchestrator",
            "AgentEvolutionProposal", proposalId.ToString(),
            "BackfillTargetProject",
            new { proposalId, targetProjectId = ConfiguredProjectId });

        var reconciler = BuildReconciler();
        await reconciler.ReconcileAsync();
        await reconciler.ReconcileAsync();

        _db.ChangeTracker.Clear();
        var events = await _auditRepo.GetAllOrderedAsync();
        var backfillEvents = events.Where(e =>
            e.EventType == AuditEventType.LegacyEvolutionTargetBackfilled
            && e.ResourceId == proposalId.ToString()).ToList();
        Assert.Single(backfillEvents);
    }

    // ==== Implemented derivation ====

    [Fact]
    public async Task Reconcile_ApprovedWithUnmergedTask_DoesNotMarkImplemented()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.InProgress, null, null);

        var result = await BuildReconciler().ReconcileAsync();

        Assert.Equal(0, result.ProposalsMarkedImplemented);
        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.Equal(AgentEvolutionProposalStatus.Implementing, reloaded!.Status);
    }

    [Fact]
    public async Task Reconcile_FailedTask_DoesNotMarkImplemented()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Failed, null, null);

        var result = await BuildReconciler().ReconcileAsync();

        Assert.Equal(0, result.ProposalsMarkedImplemented);
        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.Equal(AgentEvolutionProposalStatus.Approved, reloaded!.Status);
    }

    [Fact]
    public async Task Reconcile_PrCreatedButNotMerged_DoesNotMarkImplemented()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.InProgress, mergeCommitSha: null, pullRequestNumber: 42);

        var result = await BuildReconciler().ReconcileAsync();

        Assert.Equal(0, result.ProposalsMarkedImplemented);
        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.Equal(AgentEvolutionProposalStatus.Implementing, reloaded!.Status);
        Assert.Null(reloaded.ImplementationMergeCommitSha);
    }

    [Fact]
    public async Task Reconcile_CompletedTaskWithMergeCommitSha_MarksImplementedWithPersistedEvidence()
    {
        var mergeSha = "ade17ae7a0d06f113a7f62614eb14ff1ed440ef5";
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Completed, mergeSha, pullRequestNumber: 11);

        var result = await BuildReconciler().ReconcileAsync();

        Assert.Equal(1, result.ProposalsMarkedImplemented);
        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.Equal(AgentEvolutionProposalStatus.Implemented, reloaded!.Status);
        Assert.Equal(mergeSha, reloaded.ImplementationMergeCommitSha);
        Assert.Equal(11, reloaded.ImplementationPullRequestNumber);
        Assert.NotNull(reloaded.ImplementedAt);
    }

    [Fact]
    public async Task Reconcile_AwaitingReviewTaskWithPersistedMerge_MarksImplemented()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(
            AgentTaskStatus.AwaitingReview,
            mergeCommitSha: "ade17ae7a0d06f113a7f62614eb14ff1ed440ef5",
            pullRequestNumber: 11);

        var result = await BuildReconciler().ReconcileAsync();

        Assert.Equal(1, result.ProposalsMarkedImplemented);
        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.Equal(AgentEvolutionProposalStatus.Implemented, reloaded!.Status);
    }

    [Fact]
    public async Task Reconcile_InvalidConfiguredProject_DoesNotRepairBlankTarget()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Created, null, null, targetProjectId: "");

        var reconciler = BuildReconciler(projectId: "default", registryOverride: new TestProjectRegistry("default"));
        var result = await reconciler.ReconcileAsync();

        Assert.Equal(0, result.TargetProjectsRepaired);
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.Empty(reloaded!.TargetProjectId);
    }

    [Fact]
    public async Task Reconcile_MarkImplemented_EmitsExactlyOneAgentEvolutionImplementedEvent()
    {
        var mergeSha = "abc1234567890";
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Completed, mergeSha, pullRequestNumber: 11);

        var reconciler = BuildReconciler();
        await reconciler.ReconcileAsync();
        await reconciler.ReconcileAsync();
        await reconciler.ReconcileAsync();

        _db.ChangeTracker.Clear();
        var events = await _auditRepo.GetAllOrderedAsync();
        var implEvents = events.Where(e =>
            e.EventType == AuditEventType.AgentEvolutionImplemented
            && e.ResourceId == proposalId.ToString()).ToList();
        Assert.Single(implEvents);
        Assert.Equal(Core.Authority.ActorType.System, implEvents[0].ActorType);
        Assert.Equal("EvolutionLifecycleReconciler", implEvents[0].ActorId);
        Assert.Contains(mergeSha, implEvents[0].PayloadJson);
    }

    [Fact]
    public async Task Reconcile_IsIdempotent_SecondRunProducesNoAdditionalMutation()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Completed, "sha1234", 7);

        var reconciler = BuildReconciler();
        await reconciler.ReconcileAsync();
        _db.ChangeTracker.Clear();
        var afterFirst = await _proposalRepo.GetByIdAsync(proposalId);
        var eventCountAfterFirst = (await _auditRepo.GetAllOrderedAsync()).Count;

        var second = await reconciler.ReconcileAsync();

        Assert.Equal(0, second.ProposalsMarkedImplemented);
        Assert.Equal(0, second.TargetProjectsRepaired);
        _db.ChangeTracker.Clear();
        var afterSecond = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.Equal(afterFirst!.ImplementedAt, afterSecond!.ImplementedAt);
        Assert.Equal(eventCountAfterFirst, (await _auditRepo.GetAllOrderedAsync()).Count);
    }

    [Fact]
    public async Task Reconcile_DoesNotDuplicateExistingHumanApprovalAudit()
    {
        var mergeSha = "sha";
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Completed, mergeSha, 11);
        // Pre-existing human approval event.
        await _auditService.RecordAsync(
            AuditEventType.AgentEvolutionApproved,
            Core.Authority.ActorType.Human, Guid.NewGuid().ToString(),
            "AgentEvolutionProposal", proposalId.ToString(),
            "Approve", new { proposalId });

        var reconciler = BuildReconciler();
        await reconciler.ReconcileAsync();
        await reconciler.ReconcileAsync();

        _db.ChangeTracker.Clear();
        var events = await _auditRepo.GetAllOrderedAsync();
        Assert.Single(events, e =>
            e.EventType == AuditEventType.AgentEvolutionApproved
            && e.ResourceId == proposalId.ToString());
    }

    [Fact]
    public async Task Reconcile_DoesNotCreateAdditionalImplementationTask()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Completed, "sha", 11);
        var tasksBefore = await _taskRepo.GetRecentAsync(100);

        await BuildReconciler().ReconcileAsync();

        var tasksAfter = await _taskRepo.GetRecentAsync(100);
        Assert.Equal(tasksBefore.Count, tasksAfter.Count);
    }

    [Fact]
    public async Task Reconcile_AuditFailure_FailsClosedAndDoesNotChangeStatus()
    {
        var proposalId = await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Completed, "sha", 11);

        var flaky = new FlakyAuditRepository(_auditRepo) { FailOnEventType = AuditEventType.AgentEvolutionImplemented };
        var flakyService = new AuditService(flaky, NullLogger<AuditService>.Instance);
        var reconciler = BuildReconciler(auditServiceOverride: flakyService);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reconciler.ReconcileAsync());

        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.Equal(AgentEvolutionProposalStatus.Approved, reloaded!.Status);
        Assert.Null(reloaded.ImplementationMergeCommitSha);
    }

    [Fact]
    public async Task Reconcile_UsesPersistedTaskMergeEvidence_NotModelText()
    {
        // The reconciler must ignore any model-supplied text on the proposal (e.g. evidence
        // field). Only the linked AgentTask's persisted MergeCommitSha may transition Implemented.
        var task = new AgentTask(ConfiguredProjectId, "impl", "body", AgentRole.BackendDeveloper);
        // Leave the task unmerged.
        await _taskRepo.AddAsync(task);
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(),
            AgentEvolutionProposalType.ModifyAgent, ConfiguredProjectId,
            null, null, null,
            "Purpose",
            evidence: "The implementation was fully merged as commit deadbeef1234.",
            suggestedChange: "Change",
            null, null, RiskLevel.Low, "eval",
            AgentEvolutionProposalStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, task.Id);
        await _proposalRepo.AddAsync(proposal);

        var result = await BuildReconciler().ReconcileAsync();

        Assert.Equal(0, result.ProposalsMarkedImplemented);
        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposal.Id);
        Assert.Equal(AgentEvolutionProposalStatus.Approved, reloaded!.Status);
        Assert.Null(reloaded.ImplementationMergeCommitSha);
    }

    [Fact]
    public async Task Reconcile_LedgerRemainsValidAfterReconciliation()
    {
        await SeedApprovedProposalWithTaskAsync(AgentTaskStatus.Completed, "sha", 11);

        await BuildReconciler().ReconcileAsync();

        var verifier = new AuditLedgerVerifier(_auditRepo);
        var verification = await verifier.VerifyAsync();
        Assert.True(verification.IsValid);
    }
}
