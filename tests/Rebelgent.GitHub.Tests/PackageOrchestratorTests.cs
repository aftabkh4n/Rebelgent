using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Publishing;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.GitHub.Package;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;
using DomainPackage = Rebelgent.Core.Domain.Package;

namespace Rebelgent.GitHub.Tests;

public class PackageOrchestratorTests
{
    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeTaskService : ITaskService
    {
        public AgentTask? Task { get; set; }
        public System.Threading.Tasks.Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Task);
        public System.Threading.Tasks.Task<AgentTask?> TransitionAsync(Guid id, AgentTaskStatus s, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Task);
        public System.Threading.Tasks.Task<AgentTask> CreateTaskAsync(CreateTaskInput i, CancellationToken ct = default) => throw new NotImplementedException();
        public System.Threading.Tasks.Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int n = 20, CancellationToken ct = default) => throw new NotImplementedException();
        public System.Threading.Tasks.Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string p, int max, CancellationToken ct = default) => throw new NotImplementedException();
        public System.Threading.Tasks.Task<AgentTask?> SetBranchNameAsync(Guid id, string branch, CancellationToken ct = default) => throw new NotImplementedException();
        public System.Threading.Tasks.Task<AgentTask?> SetPullRequestInfoAsync(Guid id, int number, string url, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Task);
        public System.Threading.Tasks.Task<AgentTask?> SetMergeInfoAsync(Guid id, string sha, string method, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Task);
    }

    private sealed class FakePackageRepository : IPackageRepository
    {
        public DomainPackage? Existing { get; set; }
        public DomainPackage? Added { get; private set; }
        public DomainPackage? Updated { get; private set; }

        public System.Threading.Tasks.Task<DomainPackage?> GetByTaskIdAsync(Guid taskId, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Existing);
        public System.Threading.Tasks.Task AddAsync(DomainPackage package, CancellationToken ct = default)
        {
            Added = package;
            return System.Threading.Tasks.Task.CompletedTask;
        }
        public System.Threading.Tasks.Task UpdateAsync(DomainPackage package, CancellationToken ct = default)
        {
            Updated = package;
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private sealed class FakeReleaseRepository : IReleaseRepository
    {
        public Core.Domain.Release? ExistingRelease { get; set; }
        public System.Threading.Tasks.Task<Core.Domain.Release?> GetByTaskIdAsync(Guid taskId, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(ExistingRelease);
        public System.Threading.Tasks.Task AddAsync(Core.Domain.Release release, CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task UpdateAsync(Core.Domain.Release release, CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class FakeExecutionRepo : IAgentExecutionRepository
    {
        public AgentExecutionRecord? QaExecution { get; set; }
        public AgentExecutionRecord? ReviewerExecution { get; set; }

        public System.Threading.Tasks.Task<AgentExecutionRecord?> GetLatestByTaskIdAndRoleAsync(Guid taskId, AgentRole role, CancellationToken ct = default)
        {
            AgentExecutionRecord? result = role switch
            {
                AgentRole.QaEngineer => QaExecution,
                AgentRole.CodeReviewer => ReviewerExecution,
                _ => null
            };
            return System.Threading.Tasks.Task.FromResult(result);
        }
        public System.Threading.Tasks.Task AddAsync(AgentExecutionRecord r, CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task UpdateAsync(AgentExecutionRecord r, CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task<AgentExecutionRecord?> GetLatestByTaskIdAsync(Guid id, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult<AgentExecutionRecord?>(null);
        public System.Threading.Tasks.Task<IReadOnlyList<AgentExecutionRecord>> GetAllByTaskIdAsync(Guid id, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentExecutionRecord>>([]);
    }

    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public WorkspaceInfo WorkspaceResult { get; set; } =
            new(@"D:\Projects\_RebelgentWorkspaces\sandbox-abcd1234-package", "abc123");
        public Exception? ThrowOnCreate { get; set; }
        public Guid? LastTaskId { get; private set; }
        public string? LastMergeCommitSha { get; private set; }
        public string? LastRemovedPath { get; private set; }

        public Task<WorkspaceInfo> CreateForPackagingAsync(ProjectDefinition project, Guid taskId, string mergeCommitSha, CancellationToken ct = default)
        {
            if (ThrowOnCreate is not null)
                throw ThrowOnCreate;
            LastTaskId = taskId;
            LastMergeCommitSha = mergeCommitSha;
            return Task.FromResult(WorkspaceResult);
        }
        public Task RemoveAsync(string workspacePath, CancellationToken ct = default)
        {
            LastRemovedPath = workspacePath;
            return Task.CompletedTask;
        }
        public Task<WorkspaceInfo> CreateAsync(ProjectDefinition p, Guid t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkspaceInfo> CreateFromBranchAsync(ProjectDefinition p, string b, string r, string? c = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> CommitAsync(string wp, string msg, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakePackagePublisher : IPackagePublisher
    {
        public PackagePrepareResult PrepareResult { get; set; } =
            PackagePrepareResult.Ok("MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg");
        public PackagePublishedResult PublishResult { get; set; } = PackagePublishedResult.Ok();
        public PackagePrepareRequest? LastPrepareRequest { get; private set; }
        public PackagePublishRequest? LastPublishRequest { get; private set; }

        public System.Threading.Tasks.Task<PackagePrepareResult> PrepareAsync(PackagePrepareRequest request, CancellationToken ct = default)
        {
            LastPrepareRequest = request;
            return System.Threading.Tasks.Task.FromResult(PrepareResult);
        }
        public System.Threading.Tasks.Task<PackagePublishedResult> PublishAsync(PackagePublishRequest request, CancellationToken ct = default)
        {
            LastPublishRequest = request;
            return System.Threading.Tasks.Task.FromResult(PublishResult);
        }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly FakeTaskService _taskService;
        private readonly FakePackageRepository _packageRepo;
        private readonly FakeReleaseRepository _releaseRepo;
        private readonly FakeExecutionRepo _executionRepo;

        public FakeServiceProvider(FakeTaskService t, FakePackageRepository p, FakeReleaseRepository r, FakeExecutionRepo e)
        {
            _taskService = t;
            _packageRepo = p;
            _releaseRepo = r;
            _executionRepo = e;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ITaskService)) return _taskService;
            if (serviceType == typeof(IPackageRepository)) return _packageRepo;
            if (serviceType == typeof(IReleaseRepository)) return _releaseRepo;
            if (serviceType == typeof(IAgentExecutionRepository)) return _executionRepo;
            return null;
        }
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;
        public FakeScopeFactory(IServiceProvider p) => _provider = p;
        public IServiceScope CreateScope() => new FakeScope(_provider);
        private sealed class FakeScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; }
            public FakeScope(IServiceProvider p) => ServiceProvider = p;
            public void Dispose() { }
        }
    }

    private sealed class FakeProjectRegistry : IProjectRegistry
    {
        private readonly ProjectDefinition[] _projects;
        public FakeProjectRegistry(ProjectDefinition[] p) => _projects = p;
        public ProjectDefinition? Find(string id) => _projects.FirstOrDefault(p => p.Id == id);
        public IReadOnlyCollection<ProjectDefinition> GetAll() => _projects;
    }

    private static readonly ProjectDefinition SandboxProject = new()
    {
        Id = "sandbox",
        Name = "Sandbox",
        RepositoryPath = @"D:\Projects\RebelgentSandbox",
        DefaultBranch = "main"
    };

    private const string MergedSha = "abc123def456abc123def456abc123def456abc1";

    private static AgentTask MakeTask(
        AgentTaskStatus status = AgentTaskStatus.AwaitingReview,
        string? mergeCommitSha = MergedSha,
        string projectId = "sandbox")
    {
        return AgentTask.Reconstitute(
            Guid.NewGuid(), projectId, "Add login feature", "Implement OAuth login",
            AgentRole.BackendDeveloper, status, RiskLevel.Low,
            DateTimeOffset.UtcNow, null, null,
            "rebelgent/task-abc12345",
            42, "https://github.com/org/repo/pull/42", DateTimeOffset.UtcNow,
            mergeCommitSha is not null ? DateTimeOffset.UtcNow : null,
            mergeCommitSha,
            mergeCommitSha is not null ? "squash" : null);
    }

    private static Core.Domain.Release MakePublishedRelease(Guid taskId, string version = "1.0.0") =>
        Core.Domain.Release.Reconstitute(
            Guid.NewGuid(), taskId, version, $"Release {version}", "notes", false,
            ReleaseStatus.Published, $"v{version}", "https://github.com/org/repo/releases/tag/v1.0.0",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MergedSha);

    private static AgentExecutionRecord MakeQaExecution(bool passed = true)
    {
        var rec = new AgentExecutionRecord(Guid.NewGuid(), "sandbox", @"D:\ws\qa", "branch", AgentRole.QaEngineer);
        rec.MarkRunning();
        rec.CompleteWithFindings(passed ? "QA_PASSED" : "QA_FAILED", passed ? "QA_PASSED" : "QA_FAILED");
        return rec;
    }

    private static AgentExecutionRecord MakeReviewerExecution(bool approved = true)
    {
        var rec = new AgentExecutionRecord(Guid.NewGuid(), "sandbox", @"D:\ws\reviewer", "branch", AgentRole.CodeReviewer);
        rec.MarkRunning();
        rec.CompleteWithFindings(approved ? "REVIEW_APPROVED" : "Needs work.", approved ? "REVIEW_APPROVED" : "Needs work.");
        return rec;
    }

    private (PackageOrchestrator orchestrator, FakePackageRepository packageRepo, FakePackagePublisher publisher, FakeWorkspaceManager workspaceManager)
        Build(AgentTask? task, AgentExecutionRecord? qa = null, AgentExecutionRecord? reviewer = null,
            DomainPackage? existingPackage = null, Core.Domain.Release? release = null,
            ProjectDefinition[]? projects = null,
            FakeWorkspaceManager? workspaceManager = null)
    {
        var taskService = new FakeTaskService { Task = task };
        var packageRepo = new FakePackageRepository { Existing = existingPackage };
        var releaseRepo = new FakeReleaseRepository { ExistingRelease = release };
        var executionRepo = new FakeExecutionRepo { QaExecution = qa, ReviewerExecution = reviewer };
        var provider = new FakeServiceProvider(taskService, packageRepo, releaseRepo, executionRepo);
        var scopeFactory = new FakeScopeFactory(provider);
        var registry = new FakeProjectRegistry(projects ?? [SandboxProject]);
        var publisher = new FakePackagePublisher();
        workspaceManager ??= new FakeWorkspaceManager();

        var orchestrator = new PackageOrchestrator(
            scopeFactory, registry, publisher, workspaceManager, NullLogger<PackageOrchestrator>.Instance);

        return (orchestrator, packageRepo, publisher, workspaceManager);
    }

    // ── PrepareAsync — validation ─────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_TaskNotFound_ReturnsFail()
    {
        var (orchestrator, _, _, _) = Build(null);

        var result = await orchestrator.PrepareAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_TaskNotMerged_ReturnsFail()
    {
        var task = MakeTask(mergeCommitSha: null);
        var (orchestrator, _, _, _) = Build(task);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("merged", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_NoRelease_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, _, _) = Build(task, release: null);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("release", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_ReleaseNotPublished_ReturnsFail()
    {
        var task = MakeTask();
        var unpublishedRelease = Core.Domain.Release.Reconstitute(
            Guid.NewGuid(), task.Id, "1.0.0", "title", "notes", false,
            ReleaseStatus.Prepared, "v1.0.0", null,
            DateTimeOffset.UtcNow, null, MergedSha);
        var (orchestrator, _, _, _) = Build(task, release: unpublishedRelease);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Prepared", result.Summary);
    }

    [Fact]
    public async Task PrepareAsync_UnknownProject_ReturnsFail()
    {
        var task = MakeTask(projectId: "unknown");
        var release = MakePublishedRelease(task.Id);
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not registered", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_TaskInCreatedStatus_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.Created);
        var (orchestrator, _, _, _) = Build(task);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PrepareAsync_QaFailed_ReturnsFail()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id);
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(passed: false), MakeReviewerExecution(), release: release);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("QA", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_NoQaExecution_ReturnsFail()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id);
        var (orchestrator, _, _, _) = Build(task, qa: null, reviewer: MakeReviewerExecution(), release: release);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PrepareAsync_ReviewerNotApproved_ReturnsFail()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id);
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(approved: false), release: release);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("review", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_NoReviewerExecution_ReturnsFail()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id);
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(), reviewer: null, release: release);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    // ── PrepareAsync — publisher integration ─────────────────────────────────

    [Fact]
    public async Task PrepareAsync_PublisherFails_ReturnsFail()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id);
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);
        publisher.PrepareResult = PackagePrepareResult.Fail("dotnet pack failed");

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("preparation failed", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_VersionMismatch_ReturnsFail()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "2.0.0");
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);
        publisher.PrepareResult = PackagePrepareResult.Ok("MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg");

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("mismatch", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ── PrepareAsync — success ────────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_AllValid_ReturnsSuccess()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);
        publisher.PrepareResult = PackagePrepareResult.Ok("MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg");

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("MyLib", result.PackageId);
        Assert.Equal("1.0.0", result.PackageVersion);
        Assert.Equal(PackageStatus.Prepared, result.Status);
    }

    [Fact]
    public async Task PrepareAsync_AllValid_PersistsPackage()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var (orchestrator, packageRepo, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);
        publisher.PrepareResult = PackagePrepareResult.Ok("MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg");

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.NotNull(packageRepo.Added);
        Assert.Equal("MyLib", packageRepo.Added!.PackageId);
        Assert.Equal("1.0.0", packageRepo.Added.PackageVersion);
    }

    [Fact]
    public async Task PrepareAsync_AllValid_PassesExpectedVersionToPublisher()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);
        publisher.PrepareResult = PackagePrepareResult.Ok("MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg");

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.Equal("1.0.0", publisher.LastPrepareRequest!.ExpectedVersion);
    }

    [Fact]
    public async Task PrepareAsync_TaskCompleted_AlsoProceedsSuccessfully()
    {
        var task = MakeTask(AgentTaskStatus.Completed);
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);
        publisher.PrepareResult = PackagePrepareResult.Ok("MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg");

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    // ── PrepareAsync — idempotency ────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_AlreadyPrepared_ReturnsExistingWithoutRunningPublisher()
    {
        var task = MakeTask();
        var existing = new DomainPackage(task.Id, "MyLib", "0.9.0", @"C:\temp\MyLib.0.9.0.nupkg");
        var (orchestrator, _, publisher, _) = Build(task, existingPackage: existing);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("0.9.0", result.PackageVersion);
        Assert.Null(publisher.LastPrepareRequest); // publisher not called
    }

    [Fact]
    public async Task PrepareAsync_AlreadyPublished_ReturnsExistingWithoutRunningPublisher()
    {
        var task = MakeTask();
        var existing = DomainPackage.Reconstitute(
            Guid.NewGuid(), task.Id, "MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg",
            PackageStatus.Published, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var (orchestrator, _, publisher, _) = Build(task, existingPackage: existing);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(publisher.LastPrepareRequest);
    }

    [Fact]
    public async Task PrepareAsync_ExistingFailed_RerunsPublisher()
    {
        var task = MakeTask();
        var failedPackage = DomainPackage.Reconstitute(
            Guid.NewGuid(), task.Id, "MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg",
            PackageStatus.Failed, DateTimeOffset.UtcNow, null);
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var (orchestrator, packageRepo, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(),
            existingPackage: failedPackage, release: release);
        publisher.PrepareResult = PackagePrepareResult.Ok("MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg");

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(publisher.LastPrepareRequest);
        Assert.NotNull(packageRepo.Added);
    }

    // ── ApproveAndPublishAsync — validation ──────────────────────────────────

    [Fact]
    public async Task ApproveAndPublishAsync_TaskNotFound_ReturnsFail()
    {
        var (orchestrator, _, _, _) = Build(null);

        var result = await orchestrator.ApproveAndPublishAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_NoPackagePrepared_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, _, _) = Build(task);

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("no package prepared", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_PackageFailed_ReturnsFail()
    {
        var task = MakeTask();
        var failedPackage = DomainPackage.Reconstitute(
            Guid.NewGuid(), task.Id, "MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg",
            PackageStatus.Failed, DateTimeOffset.UtcNow, null);
        var (orchestrator, _, publisher, _) = Build(task, existingPackage: failedPackage);

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(publisher.LastPublishRequest);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_PackageFileNotFound_ReturnsFail()
    {
        var task = MakeTask();
        var pkg = new DomainPackage(task.Id, "MyLib", "1.0.0", @"C:\does-not-exist\MyLib.1.0.0.nupkg");
        var (orchestrator, _, publisher, _) = Build(task, existingPackage: pkg);

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(publisher.LastPublishRequest);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_PublishFails_PersistsFailedStatus()
    {
        var task = MakeTask();
        var nupkgPath = Path.Combine(Path.GetTempPath(), $"TestLib.{Guid.NewGuid():N}.nupkg");
        await File.WriteAllBytesAsync(nupkgPath, []);
        try
        {
            var pkg = new DomainPackage(task.Id, "MyLib", "1.0.0", nupkgPath);
            var (orchestrator, packageRepo, publisher, _) = Build(task, existingPackage: pkg);
            publisher.PublishResult = PackagePublishedResult.Fail("401 unauthorized");

            var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("failed", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(packageRepo.Updated);
            Assert.Equal(PackageStatus.Failed, packageRepo.Updated!.Status);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    // ── ApproveAndPublishAsync — success ──────────────────────────────────────

    [Fact]
    public async Task ApproveAndPublishAsync_AllValid_ReturnsSuccess()
    {
        var task = MakeTask();
        var nupkgPath = Path.Combine(Path.GetTempPath(), $"TestLib.{Guid.NewGuid():N}.nupkg");
        await File.WriteAllBytesAsync(nupkgPath, []);
        try
        {
            var pkg = new DomainPackage(task.Id, "MyLib", "1.0.0", nupkgPath);
            var (orchestrator, _, _, _) = Build(task, existingPackage: pkg);

            var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(PackageStatus.Published, result.Status);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task ApproveAndPublishAsync_AllValid_PersistsPublishedStatus()
    {
        var task = MakeTask();
        var nupkgPath = Path.Combine(Path.GetTempPath(), $"TestLib.{Guid.NewGuid():N}.nupkg");
        await File.WriteAllBytesAsync(nupkgPath, []);
        try
        {
            var pkg = new DomainPackage(task.Id, "MyLib", "1.0.0", nupkgPath);
            var (orchestrator, packageRepo, _, _) = Build(task, existingPackage: pkg);

            await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

            Assert.NotNull(packageRepo.Updated);
            Assert.Equal(PackageStatus.Published, packageRepo.Updated!.Status);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task ApproveAndPublishAsync_AllValid_PassesPackagePathToPublisher()
    {
        var task = MakeTask();
        var nupkgPath = Path.Combine(Path.GetTempPath(), $"TestLib.{Guid.NewGuid():N}.nupkg");
        await File.WriteAllBytesAsync(nupkgPath, []);
        try
        {
            var pkg = new DomainPackage(task.Id, "MyLib", "1.0.0", nupkgPath);
            var (orchestrator, _, publisher, _) = Build(task, existingPackage: pkg);

            await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

            Assert.Equal(nupkgPath, publisher.LastPublishRequest!.PackagePath);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    // ── ApproveAndPublishAsync — idempotency ─────────────────────────────────

    [Fact]
    public async Task ApproveAndPublishAsync_AlreadyPublished_ReturnsSuccessWithoutCallingPublisher()
    {
        var task = MakeTask();
        var publishedPackage = DomainPackage.Reconstitute(
            Guid.NewGuid(), task.Id, "MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg",
            PackageStatus.Published, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var (orchestrator, _, publisher, _) = Build(task, existingPackage: publishedPackage);

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(PackageStatus.Published, result.Status);
        Assert.Null(publisher.LastPublishRequest);
    }

    // ── GetInfoAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInfoAsync_PackageExists_ReturnsInfo()
    {
        var task = MakeTask();
        var pkg = new DomainPackage(task.Id, "MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg");
        var (orchestrator, _, _, _) = Build(task, existingPackage: pkg);

        var result = await orchestrator.GetInfoAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("MyLib", result.PackageId);
        Assert.Equal("1.0.0", result.PackageVersion);
    }

    [Fact]
    public async Task GetInfoAsync_NoPackage_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, _, _) = Build(task);

        var result = await orchestrator.GetInfoAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    // ── Safety: approve never publishes automatically ─────────────────────────

    [Fact]
    public async Task PrepareAsync_DoesNotCallPublishAsync()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);
        publisher.PrepareResult = PackagePrepareResult.Ok("MyLib", "1.0.0", @"C:\temp\MyLib.1.0.0.nupkg");

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        // PrepareAsync must never call PublishAsync
        Assert.Null(publisher.LastPublishRequest);
    }

    // ── Workspace isolation ───────────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_CreatesWorkspaceWithMergeCommitSha()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var (orchestrator, _, _, workspaceManager) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.Equal(MergedSha, workspaceManager.LastMergeCommitSha);
    }

    [Fact]
    public async Task PrepareAsync_PackRepositoryPathIsIsolatedWorkspace()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var wsManager = new FakeWorkspaceManager
        {
            WorkspaceResult = new WorkspaceInfo(@"D:\Projects\_RebelgentWorkspaces\sandbox-abcd1234-package", MergedSha)
        };
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(),
            release: release, workspaceManager: wsManager);

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.NotNull(publisher.LastPrepareRequest);
        Assert.Equal(@"D:\Projects\_RebelgentWorkspaces\sandbox-abcd1234-package",
            publisher.LastPrepareRequest!.RepositoryPath);
    }

    [Fact]
    public async Task PrepareAsync_OriginalRepositoryNotUsedAsBuildDirectory()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.NotNull(publisher.LastPrepareRequest);
        Assert.NotEqual(SandboxProject.RepositoryPath, publisher.LastPrepareRequest!.RepositoryPath,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_OutputDirectoryIsInsideIsolatedWorkspace()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        const string workspacePath = @"D:\Projects\_RebelgentWorkspaces\sandbox-abcd1234-package";
        var wsManager = new FakeWorkspaceManager
        {
            WorkspaceResult = new WorkspaceInfo(workspacePath, MergedSha)
        };
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(),
            release: release, workspaceManager: wsManager);

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.NotNull(publisher.LastPrepareRequest);
        Assert.True(
            publisher.LastPrepareRequest!.OutputDirectory.StartsWith(
                workspacePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"Output dir must be inside workspace '{workspacePath}'");
    }

    [Fact]
    public async Task PrepareAsync_WorkspaceCreationFails_ReturnsFail()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var wsManager = new FakeWorkspaceManager
        {
            ThrowOnCreate = new InvalidOperationException("git fetch failed: connection refused")
        };
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(),
            release: release, workspaceManager: wsManager);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("workspace", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(publisher.LastPrepareRequest);
    }

    [Fact]
    public async Task PrepareAsync_StaleLocalMainDoesNotAffectPackaging()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var wsManager = new FakeWorkspaceManager();
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(),
            release: release, workspaceManager: wsManager);

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.Equal(MergedSha, wsManager.LastMergeCommitSha);
        Assert.NotEqual(SandboxProject.RepositoryPath, publisher.LastPrepareRequest!.RepositoryPath,
            StringComparer.OrdinalIgnoreCase);
    }

    // ── PrepareAsync — version propagation ───────────────────────────────────

    [Fact]
    public async Task PrepareAsync_ReleaseVersionMissing_ReturnsFail()
    {
        var task = MakeTask();
        var releaseWithNoVersion = Core.Domain.Release.Reconstitute(
            Guid.NewGuid(), task.Id, "", "title", "notes", false,
            ReleaseStatus.Published, "v", "https://github.com/org/repo/releases/tag/v",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MergedSha);
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(),
            release: releaseWithNoVersion);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("version", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(publisher.LastPrepareRequest); // publisher must not be called
    }

    [Fact]
    public async Task PrepareAsync_PassesReleaseVersionAsPackageVersion()
    {
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.1.0");
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), release: release);
        publisher.PrepareResult = PackagePrepareResult.Ok("MyLib", "1.1.0", @"C:\temp\MyLib.1.1.0.nupkg");

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.NotNull(publisher.LastPrepareRequest);
        Assert.Equal("1.1.0", publisher.LastPrepareRequest!.PackageVersion);
    }

    // ── Safety: PackageId passthrough ─────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_PassesExpectedPackageIdFromProjectConfig()
    {
        var projectWithPackageId = new ProjectDefinition
        {
            Id = "sandbox",
            Name = "Sandbox",
            RepositoryPath = @"D:\Projects\RebelgentSandbox",
            DefaultBranch = "main",
            NuGetPackageId = "Rebelgent.Core"
        };
        var task = MakeTask();
        var release = MakePublishedRelease(task.Id, "1.0.0");
        var (orchestrator, _, publisher, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(),
            release: release, projects: [projectWithPackageId]);
        publisher.PrepareResult = PackagePrepareResult.Ok("Rebelgent.Core", "1.0.0", @"C:\temp\Rebelgent.Core.1.0.0.nupkg");

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.Equal("Rebelgent.Core", publisher.LastPrepareRequest!.ExpectedPackageId);
    }
}
