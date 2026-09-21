using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rebelgent.ClaudeCode.Evolution;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Proves that <see cref="AgentEvolutionOrchestrator.ApproveAsync"/> and
/// <see cref="AgentEvolutionOrchestrator.RejectAsync"/> are atomic with their audit event.
/// A failure to persist the audit event rolls back the proposal status change.
/// </summary>
public class AgentEvolutionApprovalFailClosedTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfAgentEvolutionProposalRepository _proposalRepo;
    private readonly EfAgentDefinitionRepository _agentRepo;
    private readonly EfAgentVersionRepository _versionRepo;
    private readonly EfAuditRepository _realAuditRepo;
    private readonly AgentTaskRepository _taskRepo;
    private readonly EfUnitOfWork _unitOfWork;

    public AgentEvolutionApprovalFailClosedTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new RebelgentDbContext(options);
        _db.Database.Migrate();

        _proposalRepo = new EfAgentEvolutionProposalRepository(_db);
        _agentRepo = new EfAgentDefinitionRepository(_db);
        _versionRepo = new EfAgentVersionRepository(_db);
        _realAuditRepo = new EfAuditRepository(_db);
        _taskRepo = new AgentTaskRepository(_db);
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

    private sealed class NoOpManagerAgent : IAgentEvolutionManagerAgent
    {
        public Task<AgentEvolutionAnalysisOutput> AnalyzeAsync(AgentEvolutionAnalysisInput input, CancellationToken ct = default)
            => Task.FromResult(new AgentEvolutionAnalysisOutput { Succeeded = false });
    }

    private (AgentEvolutionOrchestrator orch, FlakyAuditRepository flaky) BuildOrchestrator(
        ITaskService? taskService = null,
        IProjectRegistry? projectRegistry = null,
        string projectId = "sandbox")
    {
        var flaky = new FlakyAuditRepository(_realAuditRepo);
        var auditService = new AuditService(flaky, NullLogger<AuditService>.Instance);
        var scopeFactory = TestSecurityScopeFactory.Build();
        var authService = new HumanAuthorizationService(scopeFactory, NullLogger<HumanAuthorizationService>.Instance);
        var options = Microsoft.Extensions.Options.Options.Create(new AgentEvolutionOptions { ProjectId = projectId });
        taskService ??= new TaskService(_taskRepo, new TaskLifecycleService(), NullLogger<TaskService>.Instance);
        projectRegistry ??= new TestProjectRegistry(projectId);
        var orch = new AgentEvolutionOrchestrator(
            new NoOpManagerAgent(), _proposalRepo, _agentRepo,
            auditService, authService, _unitOfWork, projectRegistry, taskService, options,
            NullLogger<AgentEvolutionOrchestrator>.Instance);
        return (orch, flaky);
    }

    private static HumanPrincipal FullyCapableHuman() =>
        new(Guid.NewGuid(), "Test", "user-1",
            Enum.GetValues<HumanCapability>().ToHashSet(),
            DateTimeOffset.UtcNow);

    private async Task<Guid> SeedProposalAsync()
    {
        var proposal = new AgentEvolutionProposal(
            AgentEvolutionProposalType.CreateAgent,
            targetProjectId: "sandbox",
            purpose: "Test proposal",
            evidence: "Test evidence",
            suggestedChange: "Test change",
            riskLevel: RiskLevel.Low);
        // Move to AwaitingApproval so Approve is legal.
        proposal.BeginEvaluation();
        proposal.CompleteEvaluation("summary");
        await _proposalRepo.AddAsync(proposal);
        return proposal.Id;
    }

    private async Task<Guid> SeedLegacyApprovedProposalAsync(string targetProjectId = "")
    {
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(),
            AgentEvolutionProposalType.ModifyAgent,
            targetProjectId,
            null,
            "Legacy Agent",
            AgentRole.ImprovementAnalyst,
            "Resolve compatibility mapping",
            "Historical role collision evidence",
            "Introduce a distinct AgentEvolutionManager role",
            null,
            null,
            RiskLevel.High,
            "Approved before implementation-task bridge",
            AgentEvolutionProposalStatus.Approved,
            DateTimeOffset.UtcNow.AddDays(-10),
            DateTimeOffset.UtcNow.AddDays(-9),
            null);
        await _proposalRepo.AddAsync(proposal);
        return proposal.Id;
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

    private sealed class ThrowingTaskService : ITaskService
    {
        public Task<AgentTask> CreateTaskAsync(CreateTaskInput input, CancellationToken cancellationToken = default) =>
            Task.FromException<AgentTask>(new InvalidOperationException("simulated task creation failure"));

        public Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<AgentTask?>(null);
        public Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int count = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<AgentTask>>([]);
        public Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string prefix, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<AgentTask>>([]);
        public Task<AgentTask?> TransitionAsync(Guid id, AgentTaskStatus newStatus, CancellationToken cancellationToken = default) => Task.FromResult<AgentTask?>(null);
        public Task<AgentTask?> SetBranchNameAsync(Guid id, string branchName, CancellationToken cancellationToken = default) => Task.FromResult<AgentTask?>(null);
        public Task<AgentTask?> SetPullRequestInfoAsync(Guid id, int pullRequestNumber, string pullRequestUrl, CancellationToken cancellationToken = default) => Task.FromResult<AgentTask?>(null);
        public Task<AgentTask?> SetMergeInfoAsync(Guid id, string mergeCommitSha, string mergeMethod, CancellationToken cancellationToken = default) => Task.FromResult<AgentTask?>(null);
    }

    [Fact]
    public async Task Approve_AuditPersistenceFails_RollsBackProposalStatus()
    {
        var (orch, flaky) = BuildOrchestrator();
        var proposalId = await SeedProposalAsync();

        flaky.NextAppendThrows = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.ApproveAsync(proposalId, FullyCapableHuman()));

        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(reloaded);
        Assert.Equal(AgentEvolutionProposalStatus.AwaitingApproval, reloaded.Status);
        Assert.Null(reloaded.ApprovedAt);
    }

    [Fact]
    public async Task Reject_AuditPersistenceFails_RollsBackProposalStatus()
    {
        var (orch, flaky) = BuildOrchestrator();
        var proposalId = await SeedProposalAsync();

        flaky.NextAppendThrows = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.RejectAsync(proposalId, FullyCapableHuman()));

        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(reloaded);
        Assert.Equal(AgentEvolutionProposalStatus.AwaitingApproval, reloaded.Status);
    }

    [Fact]
    public async Task Approve_Succeeds_AuditEventPersistedAtomically()
    {
        var (orch, _) = BuildOrchestrator();
        var proposalId = await SeedProposalAsync();

        var result = await orch.ApproveAsync(proposalId, FullyCapableHuman());

        Assert.Equal(AgentEvolutionProposalStatus.Approved, result.Status);
        Assert.NotNull(result.ApprovedAt);

        _db.ChangeTracker.Clear();
        var events = await _realAuditRepo.GetAllOrderedAsync();
        Assert.Contains(events, e =>
            e.EventType == AuditEventType.AgentEvolutionApproved
            && e.ResourceId == proposalId.ToString()
            && e.PayloadJson.Contains("createdTaskId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Approve_CreatesOneCreatedTask_AndRepeatedApprovalReturnsSameTask()
    {
        var (orch, _) = BuildOrchestrator();
        var proposalId = await SeedProposalAsync();

        var first = await orch.ApproveAsync(proposalId, FullyCapableHuman());
        var second = await orch.ApproveAsync(proposalId, FullyCapableHuman());
        var tasks = await _taskRepo.GetRecentAsync(10);

        Assert.NotNull(first.CreatedTaskId);
        Assert.Equal(first.CreatedTaskId, second.CreatedTaskId);
        Assert.Single(tasks);
        Assert.Equal(first.CreatedTaskId, tasks.Single().Id);
        Assert.Equal(AgentTaskStatus.Created, tasks.Single().Status);
        Assert.Contains("proposalId", tasks.Single().Description);
        Assert.Contains("MUST NOT grant, modify, replay, or impersonate human authority", tasks.Single().Description);
    }

    [Fact]
    public async Task Approve_TaskCreationFails_RollsBackProposalTaskAndAudit()
    {
        var (orch, _) = BuildOrchestrator(new ThrowingTaskService());
        var proposalId = await SeedProposalAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.ApproveAsync(proposalId, FullyCapableHuman()));

        _db.ChangeTracker.Clear();
        var proposal = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(proposal);
        Assert.Equal(AgentEvolutionProposalStatus.AwaitingApproval, proposal.Status);
        Assert.Null(proposal.CreatedTaskId);
        Assert.DoesNotContain(await _realAuditRepo.GetAllOrderedAsync(), e =>
            e.EventType == AuditEventType.AgentEvolutionApproved && e.ResourceId == proposalId.ToString());
    }

    [Fact]
    public async Task Approve_UnregisteredTarget_FailsClosed()
    {
        var (orch, _) = BuildOrchestrator(projectRegistry: new TestProjectRegistry("other"));
        var proposalId = await SeedProposalAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.ApproveAsync(proposalId, FullyCapableHuman()));

        var proposal = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(proposal);
        Assert.Equal(AgentEvolutionProposalStatus.AwaitingApproval, proposal.Status);
        Assert.Empty(await _taskRepo.GetRecentAsync(10));
    }

    [Fact]
    public async Task Approve_DoesNotMutateAgentOrVersion_AndPreservesValidAuditChain()
    {
        var agent = new AgentDefinition(
            "Existing agent", AgentRole.BackendDeveloper, "Existing purpose", "Existing description", Guid.NewGuid());
        await _agentRepo.AddAsync(agent);
        var version = new AgentVersion(agent.Id, "1.0.0", "Existing prompt", "Existing capability");
        await _versionRepo.AddAsync(version);
        var (orch, _) = BuildOrchestrator();
        var proposalId = await SeedProposalAsync();

        await orch.ApproveAsync(proposalId, FullyCapableHuman());

        _db.ChangeTracker.Clear();
        var reloadedAgent = await _agentRepo.GetByIdAsync(agent.Id);
        var reloadedVersions = await _versionRepo.GetForAgentAsync(agent.Id);
        var verification = await new AuditLedgerVerifier(_realAuditRepo).VerifyAsync();

        Assert.NotNull(reloadedAgent);
        Assert.Equal(agent.Name, reloadedAgent.Name);
        Assert.Equal(agent.Role, reloadedAgent.Role);
        Assert.Equal(agent.Status, reloadedAgent.Status);
        Assert.Single(reloadedVersions);
        Assert.Equal(version.Id, reloadedVersions[0].Id);
        Assert.Equal(version.Version, reloadedVersions[0].Version);
        Assert.Equal(version.Status, reloadedVersions[0].Status);
        Assert.True(verification.IsValid);
    }

    [Fact]
    public async Task Approve_UnauthenticatedHuman_ThrowsBeforeAnyStateChange()
    {
        var (orch, _) = BuildOrchestrator();
        var proposalId = await SeedProposalAsync();

        await Assert.ThrowsAsync<HumanAuthorizationException>(() =>
            orch.ApproveAsync(proposalId, null!));

        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(reloaded);
        Assert.Equal(AgentEvolutionProposalStatus.AwaitingApproval, reloaded.Status);
    }

    [Fact]
    public async Task Approve_LegacyApprovedProposal_BackfillsTargetAndCreatesOneTask()
    {
        var (orch, _) = BuildOrchestrator();
        var proposalId = await SeedLegacyApprovedProposalAsync();

        var result = await orch.ApproveAsync(proposalId, FullyCapableHuman());

        Assert.Equal("sandbox", result.TargetProjectId);
        Assert.Equal(AgentEvolutionProposalStatus.Approved, result.Status);
        Assert.NotNull(result.CreatedTaskId);
        var task = (await _taskRepo.GetRecentAsync(10)).Single();
        Assert.Equal("sandbox", task.ProjectId);
        Assert.Equal(AgentTaskStatus.Created, task.Status);
    }

    [Fact]
    public async Task Approve_LegacyRecovery_DoesNotCreateSecondApprovalEvent_AndRecordsTaskEvent()
    {
        var (orch, _) = BuildOrchestrator();
        var proposalId = await SeedLegacyApprovedProposalAsync();

        await orch.ApproveAsync(proposalId, FullyCapableHuman());

        var events = await _realAuditRepo.GetAllOrderedAsync();
        Assert.DoesNotContain(events, e =>
            e.EventType == AuditEventType.AgentEvolutionApproved && e.ResourceId == proposalId.ToString());
        Assert.Single(events, e =>
            e.EventType == AuditEventType.LegacyEvolutionTargetBackfilled && e.ResourceId == proposalId.ToString());
        Assert.Single(events, e =>
            e.EventType == AuditEventType.EvolutionImplementationTaskCreated && e.ResourceId == proposalId.ToString());
    }

    [Fact]
    public async Task Approve_LegacyRecovery_IsIdempotent()
    {
        var (orch, _) = BuildOrchestrator();
        var proposalId = await SeedLegacyApprovedProposalAsync();

        var first = await orch.ApproveAsync(proposalId, FullyCapableHuman());
        var second = await orch.ApproveAsync(proposalId, FullyCapableHuman());

        Assert.Equal(first.CreatedTaskId, second.CreatedTaskId);
        Assert.Single(await _taskRepo.GetRecentAsync(10));
        Assert.Single(await _realAuditRepo.GetAllOrderedAsync(), e =>
            e.EventType == AuditEventType.EvolutionImplementationTaskCreated);
    }

    [Fact]
    public async Task Approve_LegacyRecovery_InvalidConfiguredProject_FailsClosedWithoutBackfill()
    {
        var (orch, _) = BuildOrchestrator(projectRegistry: new TestProjectRegistry("other"));
        var proposalId = await SeedLegacyApprovedProposalAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.ApproveAsync(proposalId, FullyCapableHuman()));

        var proposal = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(proposal);
        Assert.Empty(proposal.TargetProjectId);
        Assert.Null(proposal.CreatedTaskId);
        Assert.Empty(await _taskRepo.GetRecentAsync(10));
    }

    [Fact]
    public async Task Approve_LegacyRecovery_NeverUsesDefaultOrFilesystemPath()
    {
        var defaultOrPathCases = new[] { "default", @"D:\Projects\Rebelgent", "" };
        foreach (var configuredProjectId in defaultOrPathCases)
        {
            var (orch, _) = BuildOrchestrator(
                projectRegistry: new TestProjectRegistry("sandbox"),
                projectId: configuredProjectId);
            var proposalId = await SeedLegacyApprovedProposalAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orch.ApproveAsync(proposalId, FullyCapableHuman()));
        }
    }

    [Fact]
    public async Task Approve_ExistingTarget_IsNeverOverwritten()
    {
        var (orch, _) = BuildOrchestrator(
            projectRegistry: new TestProjectRegistry("existing"),
            projectId: "sandbox");
        var proposalId = await SeedLegacyApprovedProposalAsync("existing");

        await orch.ApproveAsync(proposalId, FullyCapableHuman());

        var proposal = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(proposal);
        Assert.Equal("existing", proposal.TargetProjectId);
    }

    [Fact]
    public async Task Approve_LegacyRecovery_TaskFailureRollsBackBackfillAndTaskAudit()
    {
        var (orch, _) = BuildOrchestrator(new ThrowingTaskService());
        var proposalId = await SeedLegacyApprovedProposalAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.ApproveAsync(proposalId, FullyCapableHuman()));

        _db.ChangeTracker.Clear();
        var proposal = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(proposal);
        Assert.Empty(proposal.TargetProjectId);
        Assert.Null(proposal.CreatedTaskId);
        Assert.Empty(await _realAuditRepo.GetAllOrderedAsync());
    }

    [Fact]
    public async Task Approve_HumanLacksCapability_ThrowsBeforeAnyStateChange()
    {
        var (orch, _) = BuildOrchestrator();
        var proposalId = await SeedProposalAsync();

        var limitedHuman = new HumanPrincipal(
            Guid.NewGuid(), "Test", "user-1",
            new HashSet<HumanCapability> { HumanCapability.ApproveTask }, // NOT ApproveAgentEvolution
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<HumanAuthorizationException>(() =>
            orch.ApproveAsync(proposalId, limitedHuman));

        _db.ChangeTracker.Clear();
        var reloaded = await _proposalRepo.GetByIdAsync(proposalId);
        Assert.NotNull(reloaded);
        Assert.Equal(AgentEvolutionProposalStatus.AwaitingApproval, reloaded.Status);
    }
}
