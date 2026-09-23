using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;
using Rebelgent.Orchestration.Agents;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Orchestrator;
using Rebelgent.Orchestration.Process;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

public class TaskRetryTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly ServiceProvider services;
    private readonly Workspace workspace = new();
    private readonly Runner runner = new();
    private readonly ExecutionConcurrencyGuard guard = new();
    private readonly AuditFault fault = new();
    private readonly TaskOrchestrator orchestrator;
    private static HumanPrincipal Human => new(Guid.NewGuid(), "Tester", "42", Enum.GetValues<HumanCapability>().ToHashSet(), DateTimeOffset.UtcNow);

    public TaskRetryTests()
    {
        connection.Open();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddScoped(_ => new RebelgentDbContext(new DbContextOptionsBuilder<RebelgentDbContext>().UseSqlite(connection).Options));
        collection.AddScoped<IAgentTaskRepository, AgentTaskRepository>();
        collection.AddScoped<IAgentExecutionRepository, AgentExecutionRepository>();
        collection.AddScoped<IReleaseRepository, EfReleaseRepository>();
        collection.AddScoped<IPackageRepository, EfPackageRepository>();
        collection.AddScoped<IUnitOfWork, EfUnitOfWork>();
        collection.AddScoped<ITaskService, TaskService>();
        collection.AddSingleton<TaskLifecycleService>();
        collection.AddScoped<IAuditRepository>(sp => new FaultyAudit(new EfAuditRepository(sp.GetRequiredService<RebelgentDbContext>()), fault));
        collection.AddScoped<IAuditService, AuditService>();
        collection.AddSingleton<IHumanAuthorizationService>(new HumanAuthorizationService(TestSecurityScopeFactory.Build(), NullLogger<HumanAuthorizationService>.Instance));
        services = collection.BuildServiceProvider();
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RebelgentDbContext>().Database.Migrate();
        orchestrator = new TaskOrchestrator(services.GetRequiredService<IServiceScopeFactory>(), new Registry(), workspace, runner,
            new Process(), guard, Microsoft.Extensions.Options.Options.Create(new ExecutionOptions()), NullLogger<TaskOrchestrator>.Instance);
    }

    private async Task<Guid> Seed(AgentTaskStatus status = AgentTaskStatus.Failed, bool history = true)
    {
        using var scope = services.CreateScope();
        var task = new AgentTask("rebelgent", "Retry", "Fix task", AgentRole.BackendDeveloper);
        new TaskLifecycleService().Transition(task, status);
        await scope.ServiceProvider.GetRequiredService<IAgentTaskRepository>().AddAsync(task);
        if (history)
        {
            var prior = new AgentExecutionRecord(task.Id, task.ProjectId, "old-workspace", "old-branch");
            prior.Fail("Original failure");
            await scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>().AddAsync(prior);
        }
        return task.Id;
    }

    private async Task<(AgentTask task, IReadOnlyList<AgentExecutionRecord> history, IReadOnlyList<AuditEvent> audit)> Read(Guid id)
    {
        using var scope = services.CreateScope();
        return ((await scope.ServiceProvider.GetRequiredService<ITaskService>().GetTaskAsync(id))!,
            await scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>().GetAllByTaskIdAsync(id),
            await scope.ServiceProvider.GetRequiredService<IAuditRepository>().GetAllOrderedAsync());
    }

    [Theory]
    [InlineData("Source repository has uncommitted changes.")]
    [InlineData("git fetch failed.")]
    [InlineData("Workspace creation failed.")]
    [InlineData("Branch preparation failed.")]
    public async Task PreparationFailure_PreservesFailedTaskAndHistory(string message)
    {
        var id = await Seed();
        workspace.Error = message;
        var result = await orchestrator.RetryAsync(id, Human);
        var state = await Read(id);
        Assert.False(result.Succeeded);
        Assert.Contains(message, result.Summary);
        Assert.Equal(AgentTaskStatus.Failed, state.task.Status);
        Assert.Equal("Original failure", Assert.Single(state.history).ErrorMessage);
        Assert.Equal(new[] { AuditEventType.TaskRetryRequested, AuditEventType.TaskRetryPreflightFailed }, state.audit.Select(e => e.EventType));
        Assert.Equal(0, runner.Runs);
    }

    [Fact]
    public async Task ClaudePreflightFailure_DoesNotPrepareOrIncrementAttempt()
    {
        var id = await Seed();
        runner.Ready = false;
        await orchestrator.RetryAsync(id, Human);
        var state = await Read(id);
        Assert.Equal(AgentTaskStatus.Failed, state.task.Status);
        Assert.Single(state.history);
        Assert.Equal(0, workspace.Creates);
    }

    [Theory]
    [InlineData(AgentTaskStatus.Failed)]
    [InlineData(AgentTaskStatus.Planning)]
    public async Task AuditFailure_RollsBackEveryTransitionAndNewExecution(AgentTaskStatus status)
    {
        var id = await Seed(status);
        fault.Event = AuditEventType.TaskRetryStarted;
        await orchestrator.RetryAsync(id, Human);
        var state = await Read(id);
        Assert.Equal(status, state.task.Status);
        Assert.Single(state.history);
        Assert.Null(state.task.BranchName);
        Assert.DoesNotContain(state.audit, e => e.EventType == AuditEventType.TaskRetryStarted);
        Assert.Equal(0, runner.Runs);
    }

    [Fact]
    public async Task StrandedPlanning_RecoversAndStartsAttemptTwoPreservingHistory()
    {
        var id = await Seed(AgentTaskStatus.Planning);
        await orchestrator.RetryAsync(id, Human);
        var state = await Read(id);
        Assert.Equal(2, state.history.Count);
        Assert.Contains(state.history, e => e.ErrorMessage == "Original failure");
        Assert.Equal(1, runner.Runs);
        var started = Assert.Single(state.audit, e => e.EventType == AuditEventType.TaskRetryStarted);
        Assert.Contains("\"attemptNumber\":2", started.PayloadJson);
        Assert.Equal(AgentTaskStatus.Failed, state.task.Status); // fake Developer deliberately fails
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task OrdinaryPlanningOrExistingWorkspace_CannotRecover(bool priorFailure, bool existingWorkspace)
    {
        var id = await Seed(AgentTaskStatus.Planning, priorFailure);
        workspace.Exists = existingWorkspace;
        var result = await orchestrator.RetryAsync(id, Human);
        Assert.False(result.Succeeded);
        Assert.Equal(AgentTaskStatus.Planning, (await Read(id)).task.Status);
        Assert.Equal(0, workspace.Creates);
        Assert.Equal(0, runner.Runs);
    }

    [Theory]
    [InlineData(AgentTaskStatus.Planning, "already in Planning")]
    [InlineData(AgentTaskStatus.Failed, "/retry")]
    public async Task Run_RefusesInvalidStatusBeforePreparation(AgentTaskStatus status, string message)
    {
        var id = await Seed(status);
        var result = await orchestrator.RunAsync(id);
        Assert.Contains(message, result.Summary);
        Assert.Equal(status, (await Read(id)).task.Status);
        Assert.Equal(0, workspace.Creates);
    }

    [Fact]
    public async Task ConcurrentRetryAndRun_OnlyOneExecutionStarts()
    {
        var id = await Seed();
        runner.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = orchestrator.RetryAsync(id, Human);
        await runner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await orchestrator.RetryAsync(id, Human);
        var run = await orchestrator.RunAsync(id);
        Assert.False(second.Succeeded);
        Assert.False(run.Succeeded);
        runner.Block.SetResult();
        await first;
        Assert.Equal(1, runner.Runs);
        Assert.Equal(2, (await Read(id)).history.Count);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("success")]
    [InlineData("commit")]
    [InlineData("pr")]
    [InlineData("merge")]
    public async Task PlanningWithProtectedHistory_CannotRecover(string protectedState)
    {
        var id = await Seed(AgentTaskStatus.Planning);
        using (var scope = services.CreateScope())
        {
            if (protectedState is "pr" or "merge")
            {
                var tasks = scope.ServiceProvider.GetRequiredService<ITaskService>();
                if (protectedState == "pr") await tasks.SetPullRequestInfoAsync(id, 1, "https://example.test/pr/1");
                else await tasks.SetMergeInfoAsync(id, "merge-sha", "squash");
            }
            else
            {
                var record = new AgentExecutionRecord(id, "rebelgent", "protected", "branch");
                if (protectedState == "active") record.MarkRunning();
                else if (protectedState == "success") record.Complete("", "", "", true, true);
                else { record.Fail("Failed after commit"); record.SetCommitSha("sha"); }
                await scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>().AddAsync(record);
            }
        }
        Assert.False((await orchestrator.RetryAsync(id, Human)).Succeeded);
        Assert.Equal(AgentTaskStatus.Planning, (await Read(id)).task.Status);
        Assert.Equal(0, workspace.Creates);
        Assert.Equal(0, runner.Runs);
    }

    [Fact]
    public async Task FailedStartAudit_CleansOnlyPreparedWorkspace()
    {
        var id = await Seed();
        fault.Event = AuditEventType.TaskRetryStarted;
        await orchestrator.RetryAsync(id, Human);
        Assert.Equal(1, workspace.Removes);
        Assert.Equal(AgentTaskStatus.Failed, (await Read(id)).task.Status);
    }

    [Fact]
    public async Task RequestedAuditFailure_PreventsPreparation()
    {
        var id = await Seed();
        fault.Event = AuditEventType.TaskRetryRequested;
        await orchestrator.RetryAsync(id, Human);
        Assert.Equal(0, workspace.Creates);
        Assert.Single((await Read(id)).history);
    }

    private sealed class AuditFault { public string? Event; }
    private sealed class FaultyAudit(IAuditRepository inner, AuditFault fault) : IAuditRepository
    {
        public Task<AuditEvent> AppendAsync(AuditEvent audit, CancellationToken ct = default)
        {
            if (audit.EventType == fault.Event) throw new InvalidOperationException("Audit append failed");
            return inner.AppendAsync(audit, ct);
        }
        public Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default) => inner.GetLatestAsync(ct);
        public Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default) => inner.GetAllOrderedAsync(ct);
        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default) => inner.GetRecentAsync(count, ct);
    }
    private sealed class Registry : IProjectRegistry
    {
        public IReadOnlyCollection<ProjectDefinition> GetAll() => [new() { Id = "rebelgent", Name = "Rebelgent", RepositoryPath = "repo" }];
        public ProjectDefinition? Find(string id) => GetAll().SingleOrDefault(p => p.Id == id);
    }
    private sealed class Workspace : IWorkspaceManager
    {
        public string? Error;
        public bool Exists;
        public int Creates;
        public int Removes;
        public Task<bool> HasDeveloperWorkspaceAsync(ProjectDefinition p, Guid id, CancellationToken ct = default) => Task.FromResult(Exists);
        public Task<WorkspaceInfo> CreateAsync(ProjectDefinition p, Guid id, CancellationToken ct = default)
        {
            Creates++;
            if (Error is not null) throw new InvalidOperationException(Error);
            return Task.FromResult(new WorkspaceInfo("new-workspace", "new-branch"));
        }
        public Task<WorkspaceInfo> CreateFromBranchAsync(ProjectDefinition p, string b, string r, string? sha = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkspaceInfo> CreateForPackagingAsync(ProjectDefinition p, Guid id, string sha, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> CommitAsync(string p, string m, CancellationToken ct = default) => Task.FromResult("sha");
        public Task RemoveAsync(string p, CancellationToken ct = default) { Removes++; return Task.CompletedTask; }
    }
    private sealed class Runner : ICodingAgentRunner
    {
        public bool Ready = true;
        public int Runs;
        public TaskCompletionSource? Block;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AgentValidationResult> ValidateAsync(CancellationToken ct = default) => Task.FromResult(new AgentValidationResult { IsReady = Ready, ErrorMessage = Ready ? null : "Claude preflight failed" });
        public async Task<CodingAgentResult> RunAsync(CodingAgentRequest request, CancellationToken ct = default)
        {
            Runs++;
            Entered.TrySetResult();
            if (Block is not null) await Block.Task;
            return new CodingAgentResult { Success = false, ErrorMessage = "Test Developer failed" };
        }
        public Task<CodingAgentResult> InvokeAsync(string p, string w, int t, CancellationToken ct = default) => throw new NotImplementedException();
    }
    private sealed class Process : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default) => throw new NotImplementedException();
    }
    public void Dispose() { services.Dispose(); guard.Dispose(); connection.Dispose(); }
}
