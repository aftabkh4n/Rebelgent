using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.GitHub;
using Rebelgent.Orchestration.Projects;

namespace Rebelgent.GitHub.Tests;

public class MergeOrchestratorTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeTaskService : ITaskService
    {
        public AgentTask? Task { get; set; }
        public (string Sha, string Method)? MergeInfoSet { get; private set; }

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
        public System.Threading.Tasks.Task<AgentTask?> SetMergeInfoAsync(Guid id, string sha, string method, CancellationToken ct = default)
        {
            MergeInfoSet = (sha, method);
            if (Task is not null) Task.SetMergeInfo(sha, method);
            return System.Threading.Tasks.Task.FromResult(Task);
        }
    }

    private sealed class FakeExecutionRepo : IAgentExecutionRepository
    {
        public AgentExecutionRecord? DevExecution { get; set; }
        public AgentExecutionRecord? QaExecution { get; set; }
        public AgentExecutionRecord? ReviewerExecution { get; set; }

        public System.Threading.Tasks.Task<AgentExecutionRecord?> GetLatestByTaskIdAndRoleAsync(Guid taskId, AgentRole role, CancellationToken ct = default)
        {
            AgentExecutionRecord? result = role switch
            {
                AgentRole.BackendDeveloper => DevExecution,
                AgentRole.QaEngineer => QaExecution,
                AgentRole.CodeReviewer => ReviewerExecution,
                _ => null
            };
            return System.Threading.Tasks.Task.FromResult(result);
        }

        public System.Threading.Tasks.Task AddAsync(AgentExecutionRecord r, CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task UpdateAsync(AgentExecutionRecord r, CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task<AgentExecutionRecord?> GetLatestByTaskIdAsync(Guid id, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(DevExecution);
        public System.Threading.Tasks.Task<IReadOnlyList<AgentExecutionRecord>> GetAllByTaskIdAsync(Guid id, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentExecutionRecord>>([]);
    }

    private sealed class FakeMergeService : IPullRequestMergeService
    {
        public bool IsReady { get; set; } = true;
        public PrStateResult PrState { get; set; } = PrStateResult.Ok("rebelgent/task-abc12345", "main", "OPEN", true);
        public PullRequestMergeResult? MergeResult { get; set; }
        public MergeRequest? LastMergeRequest { get; private set; }
        public string? ErrorForValidation { get; set; }

        public System.Threading.Tasks.Task<GitHubValidationResult> ValidateAsync(CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(IsReady
                ? GitHubValidationResult.Ready()
                : GitHubValidationResult.Unavailable(ErrorForValidation ?? "gh not available"));

        public System.Threading.Tasks.Task<PrStateResult> GetPrStateAsync(int prNumber, string repoPath, string? ghRepo, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(PrState);

        public System.Threading.Tasks.Task<PullRequestMergeResult> MergeAsync(MergeRequest request, CancellationToken ct = default)
        {
            LastMergeRequest = request;
            return System.Threading.Tasks.Task.FromResult(MergeResult ?? PullRequestMergeResult.Ok("abc123def456", "squash"));
        }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly FakeTaskService _taskService;
        private readonly FakeExecutionRepo _executionRepo;
        public FakeServiceProvider(FakeTaskService t, FakeExecutionRepo e) { _taskService = t; _executionRepo = e; }
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ITaskService)) return _taskService;
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
        DefaultBranch = "main",
        RemoteName = "origin",
        GitHubRepository = "myorg/sandbox"
    };

    private static AgentTask MakeTask(AgentTaskStatus status, string? branch = "rebelgent/task-abc12345",
        string projectId = "sandbox", bool hasPr = true, string? mergeCommitSha = null)
    {
        return AgentTask.Reconstitute(
            Guid.NewGuid(), projectId, "Add login feature", "Implement OAuth login",
            AgentRole.BackendDeveloper, status, RiskLevel.Low,
            DateTimeOffset.UtcNow, null, null,
            branch,
            hasPr ? 42 : null,
            hasPr ? "https://github.com/org/repo/pull/42" : null,
            hasPr ? DateTimeOffset.UtcNow : null,
            mergeCommitSha is not null ? DateTimeOffset.UtcNow : null,
            mergeCommitSha,
            mergeCommitSha is not null ? "squash" : null);
    }

    private static AgentExecutionRecord MakeDevExecution(string? commitSha = "abc1234567890def")
    {
        var rec = new AgentExecutionRecord(Guid.NewGuid(), "sandbox", @"D:\ws\dev", "rebelgent/task-abc12345");
        rec.MarkRunning();
        rec.Complete("output", "build ok", "tests ok", true, true);
        if (commitSha is not null)
            rec.SetCommitSha(commitSha);
        return rec;
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

    private (MergeOrchestrator orchestrator, FakeTaskService taskService, FakeMergeService mergeService)
        Build(AgentTask? task, AgentExecutionRecord? dev, AgentExecutionRecord? qa, AgentExecutionRecord? reviewer,
            bool ghReady = true, PrStateResult? prState = null, PullRequestMergeResult? mergeResult = null,
            ProjectDefinition[]? projects = null)
    {
        var taskService = new FakeTaskService { Task = task };
        var executionRepo = new FakeExecutionRepo { DevExecution = dev, QaExecution = qa, ReviewerExecution = reviewer };
        var provider = new FakeServiceProvider(taskService, executionRepo);
        var scopeFactory = new FakeScopeFactory(provider);
        var registry = new FakeProjectRegistry(projects ?? [SandboxProject]);
        var mergeService = new FakeMergeService
        {
            IsReady = ghReady,
            MergeResult = mergeResult
        };
        if (prState is not null)
            mergeService.PrState = prState;

        var orchestrator = new MergeOrchestrator(
            scopeFactory, registry, mergeService,
            NullLogger<MergeOrchestrator>.Instance);

        return (orchestrator, taskService, mergeService);
    }

    // ── Task not found ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TaskNotFound_ReturnsFail()
    {
        var (orchestrator, _, _) = Build(null, null, null, null);

        var result = await orchestrator.RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AlreadyMerged_ReturnsExistingMergeInfoWithoutCallingService()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview, mergeCommitSha: "existingsha");
        var (orchestrator, _, mergeService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("existingsha", result.MergeCommitSha);
        Assert.Null(mergeService.LastMergeRequest);
    }

    // ── Status guard ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TaskInCreatedStatus_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.Created);
        var (orchestrator, _, mergeService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_TaskInInProgressStatus_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.InProgress);
        var (orchestrator, _, mergeService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_TaskInAwaitingReview_ProceedsPastStatusCheck()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.NotNull(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_TaskCompleted_ProceedsPastStatusCheck()
    {
        var task = MakeTask(AgentTaskStatus.Completed);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.NotNull(mergeService.LastMergeRequest);
    }

    // ── Missing branch / PR ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TaskHasNoBranch_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview, branch: null);
        var (orchestrator, _, mergeService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_TaskHasNoPullRequest_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview, hasPr: false);
        var (orchestrator, _, mergeService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("pull request", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    // ── Unknown project ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_UnknownProject_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview, projectId: "unknown");
        var (orchestrator, _, mergeService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not registered", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    // ── Execution record guards ───────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoDevExecution_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, mergeService) = Build(task, dev: null, qa: null, reviewer: null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("commit SHA", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_DevExecutionMissingCommitSha_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(commitSha: null), null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("commit SHA", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_QaFailed_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(passed: false), null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("QA", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_NoQaExecution_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), qa: null, reviewer: null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_ReviewerChangesRequested_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(approved: false));

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("review", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_NoReviewerExecution_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), reviewer: null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(mergeService.LastMergeRequest);
    }

    // ── gh CLI availability ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_GhUnavailable_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, _) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(), ghReady: false);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("GitHub CLI", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ── PR state validation ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PrStateFetchFails_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var failState = PrStateResult.Fail("Could not retrieve PR state");
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(), prState: failState);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_MismatchedHeadBranch_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview, branch: "rebelgent/task-abc12345");
        var mismatchedState = PrStateResult.Ok("some-other-branch", "main", "OPEN", true);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(), prState: mismatchedState);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("head branch", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_MismatchedBaseBranch_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var mismatchedState = PrStateResult.Ok("rebelgent/task-abc12345", "develop", "OPEN", true);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(), prState: mismatchedState);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("base branch", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_PrClosed_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var closedState = PrStateResult.Ok("rebelgent/task-abc12345", "main", "CLOSED", null);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(), prState: closedState);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not open", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    [Fact]
    public async Task RunAsync_PrHasConflicts_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var conflictState = PrStateResult.Ok("rebelgent/task-abc12345", "main", "OPEN", false);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(), prState: conflictState);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("conflict", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mergeService.LastMergeRequest);
    }

    // ── Merge failure ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_MergeFails_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var failResult = PullRequestMergeResult.Fail("branch protection rule violation");
        var (orchestrator, _, _) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(), mergeResult: failResult);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("merge failed", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ── Successful flow ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AllChecksPass_ReturnsSuccessWithMergeInfo()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, _) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("abc123def456", result.MergeCommitSha);
        Assert.Equal("squash", result.MergeMethod);
    }

    [Fact]
    public async Task RunAsync_AllChecksPass_PersistsMergeInfo()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, taskService, _) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.NotNull(taskService.MergeInfoSet);
        Assert.Equal("abc123def456", taskService.MergeInfoSet!.Value.Sha);
        Assert.Equal("squash", taskService.MergeInfoSet!.Value.Method);
    }

    [Fact]
    public async Task RunAsync_AllChecksPass_UsesCorrectPrNumberInMergeRequest()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, mergeService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.NotNull(mergeService.LastMergeRequest);
        Assert.Equal(42, mergeService.LastMergeRequest!.PullRequestNumber);
    }
}
