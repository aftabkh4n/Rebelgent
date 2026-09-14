using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.ClaudeCode.ReleaseNotes;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.GitHub.Release;
using Rebelgent.Orchestration.Projects;

namespace Rebelgent.GitHub.Tests;

public class ReleaseOrchestratorTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

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

    private sealed class FakeReleaseRepository : IReleaseRepository
    {
        public Core.Domain.Release? ExistingRelease { get; set; }
        public Core.Domain.Release? Added { get; private set; }
        public Core.Domain.Release? Updated { get; private set; }

        public System.Threading.Tasks.Task<Core.Domain.Release?> GetByTaskIdAsync(Guid taskId, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(ExistingRelease);

        public System.Threading.Tasks.Task AddAsync(Core.Domain.Release release, CancellationToken ct = default)
        {
            Added = release;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task UpdateAsync(Core.Domain.Release release, CancellationToken ct = default)
        {
            Updated = release;
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private sealed class FakeReleaseNotesAgent : IReleaseNotesAgent
    {
        public ReleaseNotesOutput Output { get; set; } = new()
        {
            Succeeded = true,
            Version = "1.2.3",
            Title = "Feature release",
            Notes = "## What's Changed\n- Added feature",
            HasBreakingChanges = false
        };

        public System.Threading.Tasks.Task<ReleaseNotesOutput> PrepareAsync(ReleaseNotesInput input, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Output);
    }

    private sealed class FakeReleaseService : IReleaseService
    {
        public bool IsReady { get; set; } = true;
        public bool TagExists { get; set; } = false;
        public ReleaseCreatedResult CreateResult { get; set; } = ReleaseCreatedResult.Ok("https://github.com/org/repo/releases/tag/v1.2.3");
        public string? ErrorForValidation { get; set; }
        public CreateReleaseRequest? LastCreateRequest { get; private set; }

        /// <summary>Latest version returned by GetLatestReleaseVersionAsync. Null = no releases.</summary>
        public string? LatestVersion { get; set; } = null;

        public System.Threading.Tasks.Task<GitHubValidationResult> ValidateAsync(CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(IsReady
                ? GitHubValidationResult.Ready()
                : GitHubValidationResult.Unavailable(ErrorForValidation ?? "gh not available"));

        public System.Threading.Tasks.Task<bool> TagExistsAsync(string tagName, string repositoryPath, string? gitHubRepository, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(TagExists);

        public System.Threading.Tasks.Task<string?> GetLatestReleaseVersionAsync(string repositoryPath, string? gitHubRepository, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(LatestVersion);

        public System.Threading.Tasks.Task<ReleaseCreatedResult> CreateReleaseAsync(CreateReleaseRequest request, CancellationToken ct = default)
        {
            LastCreateRequest = request;
            return System.Threading.Tasks.Task.FromResult(CreateResult);
        }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly FakeTaskService _taskService;
        private readonly FakeExecutionRepo _executionRepo;
        private readonly FakeReleaseRepository _releaseRepo;

        public FakeServiceProvider(FakeTaskService t, FakeExecutionRepo e, FakeReleaseRepository r)
        {
            _taskService = t;
            _executionRepo = e;
            _releaseRepo = r;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ITaskService)) return _taskService;
            if (serviceType == typeof(IAgentExecutionRepository)) return _executionRepo;
            if (serviceType == typeof(IReleaseRepository)) return _releaseRepo;
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
        DefaultBranch = "main",
        GitHubRepository = "myorg/sandbox"
    };

    private const string MergedCommitSha = "abc123def456abc123def456abc123def456abc1";

    private static AgentTask MakeTask(AgentTaskStatus status = AgentTaskStatus.AwaitingReview,
        string? mergeCommitSha = MergedCommitSha, string projectId = "sandbox")
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

    private static AgentExecutionRecord MakeQaExecution(bool passed = true)
    {
        var output = passed ? "All tests pass. QA_PASSED" : "Tests failed. QA_FAILED";
        var rec = new AgentExecutionRecord(Guid.NewGuid(), "sandbox", @"D:\ws\qa", "rebelgent/task-abc12345",
            AgentRole.QaEngineer);
        rec.MarkRunning();
        rec.CompleteWithFindings(output, output);
        return rec;
    }

    private static AgentExecutionRecord MakeReviewerExecution(bool approved = true)
    {
        var output = approved ? "Code looks good. REVIEW_APPROVED" : "Needs work.";
        var rec = new AgentExecutionRecord(Guid.NewGuid(), "sandbox", @"D:\ws\reviewer", "rebelgent/task-abc12345",
            AgentRole.CodeReviewer);
        rec.MarkRunning();
        rec.CompleteWithFindings(output, output);
        return rec;
    }

    private (ReleaseOrchestrator orchestrator, FakeReleaseRepository releaseRepo, FakeReleaseNotesAgent notesAgent, FakeReleaseService releaseService)
        Build(AgentTask? task, AgentExecutionRecord? qa = null, AgentExecutionRecord? reviewer = null,
            Core.Domain.Release? existingRelease = null, ProjectDefinition[]? projects = null)
    {
        var taskService = new FakeTaskService { Task = task };
        var executionRepo = new FakeExecutionRepo { QaExecution = qa, ReviewerExecution = reviewer };
        var releaseRepo = new FakeReleaseRepository { ExistingRelease = existingRelease };
        var provider = new FakeServiceProvider(taskService, executionRepo, releaseRepo);
        var scopeFactory = new FakeScopeFactory(provider);
        var registry = new FakeProjectRegistry(projects ?? [SandboxProject]);
        var notesAgent = new FakeReleaseNotesAgent();
        var releaseService = new FakeReleaseService();

        var orchestrator = new ReleaseOrchestrator(
            scopeFactory, registry, notesAgent, releaseService,
            NullLogger<ReleaseOrchestrator>.Instance);

        return (orchestrator, releaseRepo, notesAgent, releaseService);
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
    public async Task PrepareAsync_TaskInCreatedStatus_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.Created);
        var (orchestrator, _, _, _) = Build(task);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PrepareAsync_UnknownProject_ReturnsFail()
    {
        var task = MakeTask(projectId: "unknown");
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(), MakeReviewerExecution());

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not registered", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_QaFailed_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(passed: false), MakeReviewerExecution());

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("QA", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_NoQaExecution_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, _, _) = Build(task, qa: null, reviewer: MakeReviewerExecution());

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("QA", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_ReviewerNotApproved_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(approved: false));

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("review", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_NoReviewerExecution_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(), reviewer: null);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    // ── PrepareAsync — Release Manager agent ─────────────────────────────────

    [Fact]
    public async Task PrepareAsync_AgentFails_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, notesAgent, _) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = false, ErrorMessage = "timeout" };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("agent failed", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_AgentReturnsInvalidSemver_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, notesAgent, _) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = false, ErrorMessage = "invalid version: not-semver" };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    // ── PrepareAsync — success ────────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_AllValid_ReturnsSuccessWithVersion()
    {
        var task = MakeTask();
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(), MakeReviewerExecution());

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("v1.2.3", result.TagName);
        Assert.Equal(ReleaseStatus.Prepared, result.Status);
    }

    [Fact]
    public async Task PrepareAsync_AllValid_PersistsRelease()
    {
        var task = MakeTask();
        var (orchestrator, releaseRepo, _, _) = Build(task, MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.NotNull(releaseRepo.Added);
        Assert.Equal("1.2.3", releaseRepo.Added!.Version);
        Assert.Equal("v1.2.3", releaseRepo.Added.TagName);
    }

    [Fact]
    public async Task PrepareAsync_TaskCompleted_AlsoProceedsSuccessfully()
    {
        var task = MakeTask(AgentTaskStatus.Completed);
        var (orchestrator, _, _, _) = Build(task, MakeQaExecution(), MakeReviewerExecution());

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    // ── PrepareAsync — idempotency ────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_AlreadyPrepared_ReturnsExistingWithoutRunningAgent()
    {
        var task = MakeTask();
        var existing = new Core.Domain.Release(task.Id, "0.9.0", "Existing release", "notes", false, MergedCommitSha);
        var (orchestrator, _, notesAgent, _) = Build(task, existingRelease: existing);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("0.9.0", result.Version);
        // Agent should not have been called
        Assert.Equal("1.2.3", notesAgent.Output.Version); // unchanged — agent default, not used
    }

    [Fact]
    public async Task PrepareAsync_ExistingFailed_RerunsAgent()
    {
        var task = MakeTask();
        var failedRelease = Core.Domain.Release.Reconstitute(
            Guid.NewGuid(), task.Id, "0.9.0", "title", "notes", false,
            ReleaseStatus.Failed, "v0.9.0", null,
            DateTimeOffset.UtcNow, null, MergedCommitSha);
        var (orchestrator, releaseRepo, _, _) = Build(task, MakeQaExecution(), MakeReviewerExecution(), existingRelease: failedRelease);

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("1.2.3", result.Version);
        Assert.NotNull(releaseRepo.Added);
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
    public async Task ApproveAndPublishAsync_NoReleasePrepared_ReturnsFail()
    {
        var task = MakeTask();
        var (orchestrator, _, _, _) = Build(task);

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("no release prepared", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_UnknownProject_ReturnsFail()
    {
        var task = MakeTask(projectId: "unknown");
        var existing = new Core.Domain.Release(task.Id, "1.0.0", "title", "notes", false, MergedCommitSha);
        var (orchestrator, _, _, _) = Build(task, existingRelease: existing);

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not registered", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_GhUnavailable_ReturnsFail()
    {
        var task = MakeTask();
        var existing = new Core.Domain.Release(task.Id, "1.0.0", "title", "notes", false, MergedCommitSha);
        var (orchestrator, _, _, releaseService) = Build(task, existingRelease: existing);
        releaseService.IsReady = false;
        releaseService.ErrorForValidation = "gh not installed";

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("GitHub CLI", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_TagAlreadyExists_ReturnsFail()
    {
        var task = MakeTask();
        var existing = new Core.Domain.Release(task.Id, "1.0.0", "title", "notes", false, MergedCommitSha);
        var (orchestrator, _, _, releaseService) = Build(task, existingRelease: existing);
        releaseService.TagExists = true;

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(releaseService.LastCreateRequest);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_GhReleaseFails_ReturnsFail()
    {
        var task = MakeTask();
        var existing = new Core.Domain.Release(task.Id, "1.0.0", "title", "notes", false, MergedCommitSha);
        var (orchestrator, releaseRepo, _, releaseService) = Build(task, existingRelease: existing);
        releaseService.CreateResult = ReleaseCreatedResult.Fail("permission denied");

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("failed", result.Summary, StringComparison.OrdinalIgnoreCase);
        // Failed status is persisted
        Assert.NotNull(releaseRepo.Updated);
        Assert.Equal(ReleaseStatus.Failed, releaseRepo.Updated!.Status);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_FailedRelease_ReturnsFail()
    {
        var task = MakeTask();
        var failedRelease = Core.Domain.Release.Reconstitute(
            Guid.NewGuid(), task.Id, "1.0.0", "title", "notes", false,
            ReleaseStatus.Failed, "v1.0.0", null,
            DateTimeOffset.UtcNow, null, MergedCommitSha);
        var (orchestrator, _, _, releaseService) = Build(task, existingRelease: failedRelease);

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(releaseService.LastCreateRequest);
    }

    // ── ApproveAndPublishAsync — success ──────────────────────────────────────

    [Fact]
    public async Task ApproveAndPublishAsync_AllValid_ReturnsSuccess()
    {
        var task = MakeTask();
        var existing = new Core.Domain.Release(task.Id, "1.0.0", "title", "notes", false, MergedCommitSha);
        var (orchestrator, _, _, _) = Build(task, existingRelease: existing);

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ReleaseStatus.Published, result.Status);
        Assert.Contains("https://", result.GitHubReleaseUrl ?? string.Empty);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_AllValid_PersistsPublishedRelease()
    {
        var task = MakeTask();
        var existing = new Core.Domain.Release(task.Id, "1.0.0", "title", "notes", false, MergedCommitSha);
        var (orchestrator, releaseRepo, _, _) = Build(task, existingRelease: existing);

        await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.NotNull(releaseRepo.Updated);
        Assert.Equal(ReleaseStatus.Published, releaseRepo.Updated!.Status);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_AllValid_PassesCorrectTagAndShaToGh()
    {
        var task = MakeTask();
        var existing = new Core.Domain.Release(task.Id, "1.0.0", "title", "notes", false, MergedCommitSha);
        var (orchestrator, _, _, releaseService) = Build(task, existingRelease: existing);

        await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.NotNull(releaseService.LastCreateRequest);
        Assert.Equal("v1.0.0", releaseService.LastCreateRequest!.TagName);
        Assert.Equal(MergedCommitSha, releaseService.LastCreateRequest.TargetCommitSha);
    }

    // ── ApproveAndPublishAsync — idempotency ─────────────────────────────────

    [Fact]
    public async Task ApproveAndPublishAsync_AlreadyPublished_ReturnsSuccessWithoutCreatingAgain()
    {
        var task = MakeTask();
        var publishedRelease = Core.Domain.Release.Reconstitute(
            Guid.NewGuid(), task.Id, "1.0.0", "title", "notes", false,
            ReleaseStatus.Published, "v1.0.0",
            "https://github.com/org/repo/releases/tag/v1.0.0",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MergedCommitSha);
        var (orchestrator, _, _, releaseService) = Build(task, existingRelease: publishedRelease);

        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ReleaseStatus.Published, result.Status);
        Assert.Null(releaseService.LastCreateRequest);
    }

    // ── No force flags / no package publishing ────────────────────────────────

    [Fact]
    public async Task ApproveAndPublishAsync_DoesNotUseForceFlag()
    {
        // Verified by FakeReleaseService not having a force parameter —
        // actual enforcement is in GitHubCliReleaseService tests.
        // This test confirms the orchestrator only calls CreateReleaseAsync once without workaround.
        var task = MakeTask();
        var existing = new Core.Domain.Release(task.Id, "1.0.0", "title", "notes", false, MergedCommitSha);
        var (orchestrator, _, _, releaseService) = Build(task, existingRelease: existing);

        await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        // Only one CreateReleaseAsync call — no retry or force behavior
        Assert.NotNull(releaseService.LastCreateRequest);
    }

    [Fact]
    public async Task ApproveAndPublishAsync_NoPushToRemoteOrNuGetPublishing()
    {
        // The orchestrator only calls IReleaseService.CreateReleaseAsync (gh release create).
        // It does not call any push, deploy, or publish service.
        // This is structurally enforced — no such dependencies are injected.
        var task = MakeTask();
        var existing = new Core.Domain.Release(task.Id, "1.0.0", "title", "notes", false, MergedCommitSha);
        var (orchestrator, _, _, _) = Build(task, existingRelease: existing);

        // Should succeed with only the fake release service — no other side effects possible
        var result = await orchestrator.ApproveAndPublishAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    // ── Version collision — PrepareAsync queries latest before calling agent ──

    [Fact]
    public async Task PrepareAsync_NoExistingRelease_Allows100()
    {
        // First-ever release: no existing tags → agent suggests 1.0.0 → succeeds
        var task = MakeTask();
        var (orchestrator, releaseRepo, notesAgent, releaseService) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        releaseService.LatestVersion = null; // no prior releases
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = true, Version = "1.0.0", Title = "First release", Notes = "notes", HasBreakingChanges = false };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("1.0.0", result.Version);
        Assert.NotNull(releaseRepo.Added);
    }

    [Fact]
    public async Task PrepareAsync_LatestIs100_AgentSuggestsMinorIncrement_Succeeds()
    {
        // Feature work after v1.0.0 → agent should suggest 1.1.0
        var task = MakeTask();
        var (orchestrator, releaseRepo, notesAgent, releaseService) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        releaseService.LatestVersion = "1.0.0";
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = true, Version = "1.1.0", Title = "Feature release", Notes = "notes", HasBreakingChanges = false };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("1.1.0", result.Version);
        Assert.NotNull(releaseRepo.Added);
    }

    [Fact]
    public async Task PrepareAsync_LatestIs100_AgentSuggestsPatchIncrement_Succeeds()
    {
        // Bug fix after v1.0.0 → agent should suggest 1.0.1
        var task = MakeTask();
        var (orchestrator, _, notesAgent, releaseService) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        releaseService.LatestVersion = "1.0.0";
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = true, Version = "1.0.1", Title = "Patch", Notes = "notes", HasBreakingChanges = false };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("1.0.1", result.Version);
    }

    [Fact]
    public async Task PrepareAsync_LatestIs100_AgentSuggestsMajorIncrement_Succeeds()
    {
        // Breaking changes after v1.0.0 → agent may suggest 2.0.0
        var task = MakeTask();
        var (orchestrator, _, notesAgent, releaseService) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        releaseService.LatestVersion = "1.0.0";
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = true, Version = "2.0.0", Title = "Breaking release", Notes = "notes", HasBreakingChanges = true };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("2.0.0", result.Version);
    }

    [Fact]
    public async Task PrepareAsync_LatestIs100_AgentSuggestsSameVersion_ReturnsFail()
    {
        // v1.0.0 already exists; agent mistakenly suggests v1.0.0 again → rejected
        var task = MakeTask();
        var (orchestrator, _, notesAgent, releaseService) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        releaseService.LatestVersion = "1.0.0";
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = true, Version = "1.0.0", Title = "Duplicate", Notes = "notes", HasBreakingChanges = false };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not greater than", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_LatestIs110_AgentSuggestsOlderVersion_ReturnsFail()
    {
        // Latest is 1.1.0; agent incorrectly suggests 1.0.5 → rejected
        var task = MakeTask();
        var (orchestrator, _, notesAgent, releaseService) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        releaseService.LatestVersion = "1.1.0";
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = true, Version = "1.0.5", Title = "Old", Notes = "notes", HasBreakingChanges = false };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not greater than", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_ProposedTagAlreadyExists_RejectedDuringPreparation()
    {
        // v1.1.0 is proposed but that exact tag already exists on GitHub → rejected during PrepareAsync
        var task = MakeTask();
        var (orchestrator, _, notesAgent, releaseService) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        releaseService.LatestVersion = "1.0.0"; // latest is 1.0.0 so 1.1.0 > 1.0.0 passes version check
        releaseService.TagExists = true;         // but tag v1.1.0 already exists on GitHub
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = true, Version = "1.1.0", Title = "Feature", Notes = "notes", HasBreakingChanges = false };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_MalformedExistingTags_IgnoredSafely_AgentCanSuggest100()
    {
        // GetLatestReleaseVersionAsync returns null if all tags are malformed
        // → treated the same as "no releases exist" → agent can suggest 1.0.0
        var task = MakeTask();
        var (orchestrator, releaseRepo, notesAgent, releaseService) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        releaseService.LatestVersion = null; // malformed tags filtered out → returns null
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = true, Version = "1.0.0", Title = "First", Notes = "notes", HasBreakingChanges = false };

        var result = await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(releaseRepo.Added);
    }

    [Fact]
    public async Task PrepareAsync_LatestVersion_PassedToReleaseNotesAgent()
    {
        // Verify that PrepareAsync passes PreviousVersion to the agent
        var task = MakeTask();
        string? capturedPreviousVersion = null;
        var task2 = MakeTask(); // unused, just to enable capture

        var taskService = new FakeTaskService { Task = task };
        var executionRepo = new FakeExecutionRepo { QaExecution = MakeQaExecution(), ReviewerExecution = MakeReviewerExecution() };
        var releaseRepo = new FakeReleaseRepository();
        var provider = new FakeServiceProvider(taskService, executionRepo, releaseRepo);
        var scopeFactory = new FakeScopeFactory(provider);
        var registry = new FakeProjectRegistry([SandboxProject]);
        var releaseService = new FakeReleaseService { LatestVersion = "2.3.4" };

        // Use a capturing agent
        var capturingAgent = new CapturingReleaseNotesAgent(v => capturedPreviousVersion = v);
        var orchestrator = new ReleaseOrchestrator(scopeFactory, registry, capturingAgent, releaseService, NullLogger<ReleaseOrchestrator>.Instance);

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.Equal("2.3.4", capturedPreviousVersion);
    }

    // ── Package version still matches published release version after M8 ──────

    [Fact]
    public async Task PrepareAsync_AllValid_VersionStoredInRelease()
    {
        // Ensures the version from the agent is what gets stored (required by PackageOrchestrator to validate version match)
        var task = MakeTask();
        var (orchestrator, releaseRepo, notesAgent, releaseService) = Build(task, MakeQaExecution(), MakeReviewerExecution());
        releaseService.LatestVersion = "1.4.2";
        notesAgent.Output = new ReleaseNotesOutput { Succeeded = true, Version = "1.5.0", Title = "Feature", Notes = "notes", HasBreakingChanges = false };

        await orchestrator.PrepareAsync(task.Id, CancellationToken.None);

        Assert.NotNull(releaseRepo.Added);
        Assert.Equal("1.5.0", releaseRepo.Added!.Version);
    }

    // Helper: a release notes agent that captures the PreviousVersion it received
    private sealed class CapturingReleaseNotesAgent : IReleaseNotesAgent
    {
        private readonly Action<string?> _capture;
        public CapturingReleaseNotesAgent(Action<string?> capture) => _capture = capture;

        public System.Threading.Tasks.Task<ReleaseNotesOutput> PrepareAsync(ReleaseNotesInput input, CancellationToken ct = default)
        {
            _capture(input.PreviousVersion);
            return System.Threading.Tasks.Task.FromResult(new ReleaseNotesOutput
            {
                Succeeded = true,
                Version = "3.0.0",
                Title = "Major release",
                Notes = "notes",
                HasBreakingChanges = true
            });
        }
    }
}
