using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.GitHub;
using Rebelgent.Orchestration.Projects;

namespace Rebelgent.GitHub.Tests;

public class PullRequestOrchestratorTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeTaskService : ITaskService
    {
        public AgentTask? Task { get; set; }
        public (int Number, string Url)? PrInfoSet { get; private set; }

        public System.Threading.Tasks.Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Task);
        public System.Threading.Tasks.Task<AgentTask?> TransitionAsync(Guid id, AgentTaskStatus s, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Task);
        public System.Threading.Tasks.Task<AgentTask> CreateTaskAsync(CreateTaskInput i, CancellationToken ct = default) => throw new NotImplementedException();
        public System.Threading.Tasks.Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int n = 20, CancellationToken ct = default) => throw new NotImplementedException();
        public System.Threading.Tasks.Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string p, int max, CancellationToken ct = default) => throw new NotImplementedException();
        public System.Threading.Tasks.Task<AgentTask?> SetBranchNameAsync(Guid id, string branch, CancellationToken ct = default) => throw new NotImplementedException();
        public System.Threading.Tasks.Task<AgentTask?> SetPullRequestInfoAsync(Guid id, int number, string url, CancellationToken ct = default)
        {
            PrInfoSet = (number, url);
            if (Task is not null) Task.SetPullRequestInfo(number, url);
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

    private sealed class FakePullRequestService : IPullRequestService
    {
        public bool IsReady { get; set; } = true;
        public PullRequestCreatedResult? PushAndCreateResult { get; set; }
        public PushAndCreateRequest? LastRequest { get; private set; }
        public string? ErrorForValidation { get; set; }

        public System.Threading.Tasks.Task<GitHubValidationResult> ValidateAsync(CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(IsReady
                ? GitHubValidationResult.Ready()
                : GitHubValidationResult.Unavailable(ErrorForValidation ?? "gh not available"));

        public System.Threading.Tasks.Task<PullRequestCreatedResult> PushAndCreateAsync(PushAndCreateRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return System.Threading.Tasks.Task.FromResult(PushAndCreateResult ?? new PullRequestCreatedResult
            {
                Succeeded = true,
                PullRequestNumber = 42,
                PullRequestUrl = "https://github.com/org/repo/pull/42"
            });
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
        string projectId = "sandbox", string? prUrl = null)
    {
        var task = AgentTask.Reconstitute(
            Guid.NewGuid(), projectId, "Add login feature", "Implement OAuth login",
            AgentRole.BackendDeveloper, status, RiskLevel.Low,
            DateTimeOffset.UtcNow, null, null,
            branch, prUrl is null ? null : 99, prUrl, prUrl is null ? null : DateTimeOffset.UtcNow);
        return task;
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

    private (PullRequestOrchestrator orchestrator, FakeTaskService taskService, FakePullRequestService prService)
        Build(AgentTask? task, AgentExecutionRecord? dev, AgentExecutionRecord? qa, AgentExecutionRecord? reviewer,
            bool ghReady = true, PullRequestCreatedResult? prResult = null,
            ProjectDefinition[]? projects = null)
    {
        var taskService = new FakeTaskService { Task = task };
        var executionRepo = new FakeExecutionRepo { DevExecution = dev, QaExecution = qa, ReviewerExecution = reviewer };
        var provider = new FakeServiceProvider(taskService, executionRepo);
        var scopeFactory = new FakeScopeFactory(provider);
        var registry = new FakeProjectRegistry(projects ?? [SandboxProject]);
        var prService = new FakePullRequestService { IsReady = ghReady, PushAndCreateResult = prResult };

        var orchestrator = new PullRequestOrchestrator(
            scopeFactory, registry, prService,
            NullLogger<PullRequestOrchestrator>.Instance);

        return (orchestrator, taskService, prService);
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
    public async Task RunAsync_PrAlreadyCreated_ReturnsExistingPrWithoutCallingService()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview, prUrl: "https://github.com/org/repo/pull/99");
        var (orchestrator, _, prService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(99, result.PullRequestNumber);
        Assert.Null(prService.LastRequest);
    }

    // ── Status guard ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TaskInCreatedStatus_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.Created);
        var (orchestrator, _, prService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(prService.LastRequest);
    }

    [Fact]
    public async Task RunAsync_TaskInInProgressStatus_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.InProgress);
        var (orchestrator, _, prService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(prService.LastRequest);
    }

    [Fact]
    public async Task RunAsync_TaskInAwaitingReview_ProceedsPastStatusCheck()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.NotNull(prService.LastRequest);
    }

    [Fact]
    public async Task RunAsync_TaskCompleted_ProceedsPastStatusCheck()
    {
        var task = MakeTask(AgentTaskStatus.Completed);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.NotNull(prService.LastRequest);
    }

    // ── Missing branch ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TaskHasNoBranch_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview, branch: null);
        var (orchestrator, _, prService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(prService.LastRequest);
    }

    // ── Unknown project ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_UnknownProject_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview, projectId: "unknown");
        var (orchestrator, _, prService) = Build(task, null, null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not registered", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(prService.LastRequest);
    }

    // ── Execution record guards ───────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoDevExecution_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, dev: null, qa: null, reviewer: null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("commit SHA", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(prService.LastRequest);
    }

    [Fact]
    public async Task RunAsync_DevExecutionMissingCommitSha_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(commitSha: null), null, null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("commit SHA", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(prService.LastRequest);
    }

    [Fact]
    public async Task RunAsync_QaFailed_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(passed: false), null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("QA", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(prService.LastRequest);
    }

    [Fact]
    public async Task RunAsync_NoQaExecution_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), qa: null, reviewer: null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(prService.LastRequest);
    }

    [Fact]
    public async Task RunAsync_ReviewerChangesRequested_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(approved: false));

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("review", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(prService.LastRequest);
    }

    [Fact]
    public async Task RunAsync_NoReviewerExecution_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), reviewer: null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(prService.LastRequest);
    }

    // ── gh CLI availability ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_GhUnavailable_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(), ghReady: false);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("GitHub CLI", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ── Push and PR creation failures ─────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PushFails_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var failResult = new PullRequestCreatedResult { Succeeded = false, ErrorMessage = "push failed: permission denied" };
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(),
            prResult: failResult);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(prService.LastRequest);
    }

    [Fact]
    public async Task RunAsync_PrCreateFails_ReturnsFail()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var failResult = new PullRequestCreatedResult { Succeeded = false, ErrorMessage = "gh pr create failed" };
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution(),
            prResult: failResult);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    // ── Successful flow ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AllChecksPass_ReturnsPrNumberAndUrl()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.PullRequestNumber);
        Assert.Equal("https://github.com/org/repo/pull/42", result.PullRequestUrl);
    }

    [Fact]
    public async Task RunAsync_AllChecksPass_UsesDeveloperBranchNameInRequest()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview, branch: "rebelgent/task-abc12345");
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.Equal("rebelgent/task-abc12345", prService.LastRequest!.BranchName);
    }

    [Fact]
    public async Task RunAsync_AllChecksPass_UsesTaskTitleInRequest()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.Equal("Add login feature", prService.LastRequest!.Title);
    }

    [Fact]
    public async Task RunAsync_AllChecksPass_UsesConfiguredRemoteName()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.Equal("origin", prService.LastRequest!.RemoteName);
    }

    [Fact]
    public async Task RunAsync_AllChecksPass_UsesConfiguredBaseBranch()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, _, prService) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.Equal("main", prService.LastRequest!.BaseBranch);
    }

    [Fact]
    public async Task RunAsync_AllChecksPass_PersistsPrInfo()
    {
        var task = MakeTask(AgentTaskStatus.AwaitingReview);
        var (orchestrator, taskService, _) = Build(task, MakeDevExecution(), MakeQaExecution(), MakeReviewerExecution());

        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.NotNull(taskService.PrInfoSet);
        Assert.Equal(42, taskService.PrInfoSet!.Value.Number);
        Assert.Equal("https://github.com/org/repo/pull/42", taskService.PrInfoSet!.Value.Url);
    }
}
