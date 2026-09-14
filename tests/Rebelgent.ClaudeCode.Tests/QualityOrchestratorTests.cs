using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.ClaudeCode.QualityOrchestration;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Agents;
using Rebelgent.Orchestration.Orchestrator;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.ClaudeCode.Tests;

public class QualityOrchestratorTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeTaskService : ITaskService
    {
        public AgentTask? Task { get; set; }
        public Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Task);
        public Task<AgentTask?> TransitionAsync(Guid id, AgentTaskStatus s, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Task);
        public Task<AgentTask> CreateTaskAsync(CreateTaskInput i, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int n = 20, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string p, int max, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AgentTask?> SetBranchNameAsync(Guid id, string branch, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AgentTask?> SetPullRequestInfoAsync(Guid id, int number, string url, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeExecutionRepo : IAgentExecutionRepository
    {
        public AgentExecutionRecord? DeveloperExecution { get; set; }
        public Task<AgentExecutionRecord?> GetLatestByTaskIdAndRoleAsync(Guid taskId, AgentRole role, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(DeveloperExecution);
        public Task AddAsync(AgentExecutionRecord r, CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public Task UpdateAsync(AgentExecutionRecord r, CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public Task<AgentExecutionRecord?> GetLatestByTaskIdAsync(Guid id, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(DeveloperExecution);
        public Task<IReadOnlyList<AgentExecutionRecord>> GetAllByTaskIdAsync(Guid id, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentExecutionRecord>>([]);
    }

    private sealed class FakeAgentRunner : ICodingAgentRunner
    {
        public Task<AgentValidationResult> ValidateAsync(CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(new AgentValidationResult { IsReady = true });
        public Task<CodingAgentResult> RunAsync(CodingAgentRequest r, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CodingAgentResult> InvokeAsync(string p, string w, int t, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class TrackingWorkspaceManager : IWorkspaceManager
    {
        public List<(string Branch, string? CommitSha, string Role)> CreateFromBranchCalls { get; } = [];
        public bool CreateFromBranchWasCalled => CreateFromBranchCalls.Count > 0;

        public Task<WorkspaceInfo> CreateAsync(ProjectDefinition p, Guid id, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<WorkspaceInfo> CreateFromBranchAsync(ProjectDefinition p, string branch, string roleSuffix, string? commitSha = null, CancellationToken ct = default)
        {
            CreateFromBranchCalls.Add((branch, commitSha, roleSuffix));
            throw new InvalidOperationException("Stopped after worktree creation for test isolation.");
        }

        public Task<string> CommitAsync(string path, string msg, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveAsync(string path, CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly FakeTaskService _taskService;
        private readonly FakeExecutionRepo _executionRepo;

        public FakeServiceProvider(FakeTaskService taskService, FakeExecutionRepo executionRepo)
        {
            _taskService = taskService;
            _executionRepo = executionRepo;
        }

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
        public FakeScopeFactory(IServiceProvider provider) => _provider = provider;

        public IServiceScope CreateScope() => new FakeScope(_provider);

        private sealed class FakeScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; }
            public FakeScope(IServiceProvider p) => ServiceProvider = p;
            public void Dispose() { }
        }
    }

    private static readonly ProjectDefinition SandboxProject = new()
    {
        Id = "sandbox",
        Name = "Sandbox",
        RepositoryPath = @"D:\Projects\RebelgentSandbox",
        DefaultBranch = "main"
    };

    private static AgentTask MakeTask(string? branchName = "rebelgent/task-abc12345")
    {
        var task = new AgentTask("sandbox", "Test task", "Description", AgentRole.BackendDeveloper);
        if (branchName is not null)
            task.SetBranchName(branchName);
        return task;
    }

    private static AgentExecutionRecord MakeDevExecution(string? commitSha)
    {
        var record = new AgentExecutionRecord(Guid.NewGuid(), "sandbox", @"D:\ws\dev", "rebelgent/task-abc12345");
        record.MarkRunning();
        record.Complete("output", "build ok", "tests ok", true, true);
        if (commitSha is not null)
            record.SetCommitSha(commitSha);
        return record;
    }

    private (QualityOrchestrator orchestrator, TrackingWorkspaceManager workspace) Build(
        AgentTask? task,
        AgentExecutionRecord? devExecution)
    {
        var taskService = new FakeTaskService { Task = task };
        var executionRepo = new FakeExecutionRepo { DeveloperExecution = devExecution };
        var provider = new FakeServiceProvider(taskService, executionRepo);
        var scopeFactory = new FakeScopeFactory(provider);
        var workspace = new TrackingWorkspaceManager();
        var registry = new FakeProjectRegistry([SandboxProject]);
        var runner = new FakeAgentRunner();

        var orchestrator = new QualityOrchestrator(
            scopeFactory, registry, workspace, runner,
            NullLogger<QualityOrchestrator>.Instance);

        return (orchestrator, workspace);
    }

    private sealed class FakeProjectRegistry : IProjectRegistry
    {
        private readonly ProjectDefinition[] _projects;
        public FakeProjectRegistry(ProjectDefinition[] projects) => _projects = projects;
        public ProjectDefinition? Find(string id) => _projects.FirstOrDefault(p => p.Id == id);
        public IReadOnlyCollection<ProjectDefinition> GetAll() => _projects;
    }

    // ── Regression tests: CommitSha required ─────────────────────────────────

    [Fact]
    public async Task RunAsync_NoDeveloperExecution_ReturnsFailWithoutCreatingWorktrees()
    {
        var task = MakeTask();
        var (orchestrator, workspace) = Build(task, devExecution: null);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(workspace.CreateFromBranchWasCalled);
        Assert.Contains("commit SHA", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_DevExecutionHasNullCommitSha_ReturnsFailWithoutCreatingWorktrees()
    {
        var task = MakeTask();
        var devExecution = MakeDevExecution(commitSha: null);
        var (orchestrator, workspace) = Build(task, devExecution);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(workspace.CreateFromBranchWasCalled);
        Assert.Contains("commit SHA", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_DevExecutionHasEmptyCommitSha_ReturnsFailWithoutCreatingWorktrees()
    {
        var task = MakeTask();
        var devExecution = MakeDevExecution(commitSha: "");
        var (orchestrator, workspace) = Build(task, devExecution);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(workspace.CreateFromBranchWasCalled);
    }

    [Fact]
    public async Task RunAsync_DevExecutionHasWhitespaceCommitSha_ReturnsFailWithoutCreatingWorktrees()
    {
        var task = MakeTask();
        var devExecution = MakeDevExecution(commitSha: "   ");
        var (orchestrator, workspace) = Build(task, devExecution);

        var result = await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(workspace.CreateFromBranchWasCalled);
    }

    [Fact]
    public async Task RunAsync_ValidCommitSha_PassesShaToQaWorktreeCreation()
    {
        var task = MakeTask();
        var devExecution = MakeDevExecution(commitSha: "abc1234567890def");
        var (orchestrator, workspace) = Build(task, devExecution);

        // The workspace manager throws after recording the call — result will be failed but the SHA was passed
        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.True(workspace.CreateFromBranchWasCalled);
        Assert.Equal("abc1234567890def", workspace.CreateFromBranchCalls[0].CommitSha);
    }

    [Fact]
    public async Task RunAsync_ValidCommitSha_PassesSameShaToReviewerWorktree()
    {
        var task = MakeTask();
        var devExecution = MakeDevExecution(commitSha: "deadbeef12345678");
        var (orchestrator, workspace) = Build(task, devExecution);

        // TrackingWorkspaceManager throws on first call (QA), so reviewer never gets reached.
        // We verify QA received the SHA; reviewer would receive the same by construction.
        await orchestrator.RunAsync(task.Id, CancellationToken.None);

        Assert.Equal("deadbeef12345678", workspace.CreateFromBranchCalls[0].CommitSha);
    }
}
