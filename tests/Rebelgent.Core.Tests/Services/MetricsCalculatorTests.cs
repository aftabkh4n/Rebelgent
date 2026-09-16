using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Core.Tests.Services.Fakes;

namespace Rebelgent.Core.Tests.Services;

public class MetricsCalculatorTests
{
    private static (MetricsCalculator Calculator, FakeAgentTaskRepository Tasks, FakeAgentExecutionRepository Executions,
        FakeReleaseRepository Releases, FakePackageRepository Packages, FakeExecutionFailureRepository Failures) Build()
    {
        var tasks = new FakeAgentTaskRepository();
        var executions = new FakeAgentExecutionRepository();
        var releases = new FakeReleaseRepository();
        var packages = new FakePackageRepository();
        var failures = new FakeExecutionFailureRepository();
        var calculator = new MetricsCalculator(tasks, executions, releases, packages, failures);
        return (calculator, tasks, executions, releases, packages, failures);
    }

    private static readonly TaskLifecycleService Lifecycle = new();

    private static AgentTask AddTask(FakeAgentTaskRepository repo, AgentTaskStatus status)
    {
        var task = new AgentTask("sandbox", "Task", "desc", AgentRole.BackendDeveloper);
        MoveToStatus(task, status);
        repo.Tasks.Add(task);
        return task;
    }

    // Walks the task through TaskLifecycleService's valid transition graph rather than
    // reaching into AgentTask's internal state — only public API is used, same as production code.
    private static void MoveToStatus(AgentTask task, AgentTaskStatus target)
    {
        switch (target)
        {
            case AgentTaskStatus.Created:
                return;
            case AgentTaskStatus.Failed:
                Lifecycle.Transition(task, AgentTaskStatus.Failed);
                return;
            case AgentTaskStatus.InProgress:
                Lifecycle.Transition(task, AgentTaskStatus.Planning);
                Lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
                Lifecycle.Transition(task, AgentTaskStatus.Approved);
                Lifecycle.Transition(task, AgentTaskStatus.InProgress);
                return;
            case AgentTaskStatus.Completed:
                Lifecycle.Transition(task, AgentTaskStatus.Planning);
                Lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
                Lifecycle.Transition(task, AgentTaskStatus.Approved);
                Lifecycle.Transition(task, AgentTaskStatus.InProgress);
                Lifecycle.Transition(task, AgentTaskStatus.Testing);
                Lifecycle.Transition(task, AgentTaskStatus.Reviewing);
                Lifecycle.Transition(task, AgentTaskStatus.Completed);
                return;
            default:
                throw new NotSupportedException($"Test helper does not support target status {target}.");
        }
    }

    [Fact]
    public async Task ComputeAsync_NoTasks_ReturnsZeroedSnapshot()
    {
        var fixture = Build();

        var snapshot = await fixture.Calculator.ComputeAsync();

        Assert.Equal(0, snapshot.TotalTasks);
        Assert.Equal(0, snapshot.TaskSuccessRate);
        Assert.Empty(snapshot.FailuresByCategory);
    }

    [Fact]
    public async Task ComputeAsync_MixOfCompletedAndFailedTasks_ComputesSuccessRate()
    {
        var fixture = Build();
        AddTask(fixture.Tasks, AgentTaskStatus.Completed);
        AddTask(fixture.Tasks, AgentTaskStatus.Completed);
        AddTask(fixture.Tasks, AgentTaskStatus.Completed);
        AddTask(fixture.Tasks, AgentTaskStatus.Failed);

        var snapshot = await fixture.Calculator.ComputeAsync();

        Assert.Equal(4, snapshot.TotalTasks);
        Assert.Equal(0.75, snapshot.TaskSuccessRate, precision: 6);
    }

    [Fact]
    public async Task ComputeAsync_InProgressTasksExcludedFromSuccessRate()
    {
        var fixture = Build();
        AddTask(fixture.Tasks, AgentTaskStatus.Completed);
        AddTask(fixture.Tasks, AgentTaskStatus.InProgress);

        var snapshot = await fixture.Calculator.ComputeAsync();

        // Only the Completed task is terminal, so success rate is 100% over the terminal set.
        Assert.Equal(1d, snapshot.TaskSuccessRate, precision: 6);
    }

    [Fact]
    public async Task ComputeAsync_DeveloperExecutions_ComputesFailureRate()
    {
        var fixture = Build();
        var task = AddTask(fixture.Tasks, AgentTaskStatus.Completed);

        var success = new AgentExecutionRecord(task.Id, "sandbox", @"D:\ws", "branch", AgentRole.BackendDeveloper);
        success.Complete("out", "build ok", "test ok", true, true);
        fixture.Executions.Executions.Add(success);

        var failed = new AgentExecutionRecord(task.Id, "sandbox", @"D:\ws", "branch", AgentRole.BackendDeveloper);
        failed.Complete("out", "build error", "not run", false, false);
        fixture.Executions.Executions.Add(failed);

        var snapshot = await fixture.Calculator.ComputeAsync();

        Assert.Equal(0.5, snapshot.DeveloperFailureRate, precision: 6);
    }

    [Fact]
    public async Task ComputeAsync_DeveloperRetries_ComputesAverageRetriesPerTask()
    {
        var fixture = Build();
        var task = AddTask(fixture.Tasks, AgentTaskStatus.Completed);

        for (var i = 0; i < 3; i++)
        {
            var exec = new AgentExecutionRecord(task.Id, "sandbox", @"D:\ws", "branch", AgentRole.BackendDeveloper);
            exec.Complete("out", "build ok", "test ok", true, true);
            fixture.Executions.Executions.Add(exec);
        }

        var snapshot = await fixture.Calculator.ComputeAsync();

        // 3 developer executions for 1 task = 2 retries.
        Assert.Equal(2d, snapshot.AverageRetriesPerTask, precision: 6);
    }

    [Fact]
    public async Task ComputeAsync_QaExecutions_ComputesPassRate()
    {
        var fixture = Build();
        var task = AddTask(fixture.Tasks, AgentTaskStatus.Completed);

        var passed = new AgentExecutionRecord(task.Id, "sandbox", @"D:\ws", "branch", AgentRole.QaEngineer);
        passed.CompleteWithFindings("out", "QA_PASSED: all good.");
        fixture.Executions.Executions.Add(passed);

        var rejected = new AgentExecutionRecord(task.Id, "sandbox", @"D:\ws", "branch", AgentRole.QaEngineer);
        rejected.CompleteWithFindings("out", "QA_FAILED: missing validation.");
        fixture.Executions.Executions.Add(rejected);

        var snapshot = await fixture.Calculator.ComputeAsync();

        Assert.Equal(0.5, snapshot.QaPassRate, precision: 6);
    }

    [Fact]
    public async Task ComputeAsync_ReviewerExecutions_ComputesApprovalRate()
    {
        var fixture = Build();
        var task = AddTask(fixture.Tasks, AgentTaskStatus.Completed);

        var approved = new AgentExecutionRecord(task.Id, "sandbox", @"D:\ws", "branch", AgentRole.CodeReviewer);
        approved.CompleteWithFindings("out", "REVIEW_APPROVED: looks good.");
        fixture.Executions.Executions.Add(approved);

        var snapshot = await fixture.Calculator.ComputeAsync();

        Assert.Equal(1d, snapshot.ReviewApprovalRate, precision: 6);
    }

    [Fact]
    public async Task ComputeAsync_Releases_ComputesFailureRate()
    {
        var fixture = Build();
        var task1 = AddTask(fixture.Tasks, AgentTaskStatus.Completed);
        var task2 = AddTask(fixture.Tasks, AgentTaskStatus.Completed);

        var published = new Release(task1.Id, "1.0.0", "Release 1.0.0", "notes", false, new string('a', 40));
        published.Approve();
        published.SetPublished("https://example.invalid/releases/1.0.0");
        fixture.Releases.Releases.Add(published);

        var failed = new Release(task2.Id, "1.1.0", "Release 1.1.0", "notes", false, new string('b', 40));
        failed.SetFailed();
        fixture.Releases.Releases.Add(failed);

        var snapshot = await fixture.Calculator.ComputeAsync();

        Assert.Equal(0.5, snapshot.ReleaseFailureRate, precision: 6);
    }

    [Fact]
    public async Task ComputeAsync_Packages_ComputesFailureRate()
    {
        var fixture = Build();
        var task = AddTask(fixture.Tasks, AgentTaskStatus.Completed);

        var failed = new Package(task.Id, "MyLib", "1.0.0", @"C:\out\MyLib.1.0.0.nupkg");
        failed.SetFailed();
        fixture.Packages.Packages.Add(failed);

        var snapshot = await fixture.Calculator.ComputeAsync();

        Assert.Equal(1d, snapshot.PackageFailureRate, precision: 6);
    }

    [Fact]
    public async Task ComputeAsync_ExportsSnapshotToObservabilityExporter()
    {
        var tasks = new FakeAgentTaskRepository();
        var executions = new FakeAgentExecutionRepository();
        var releases = new FakeReleaseRepository();
        var packages = new FakePackageRepository();
        var failures = new FakeExecutionFailureRepository();
        var exporter = new FakeObservabilityExporter();
        var calculator = new MetricsCalculator(tasks, executions, releases, packages, failures, exporter);

        var snapshot = await calculator.ComputeAsync();

        var exported = Assert.Single(exporter.ExportedMetrics);
        Assert.Same(snapshot, exported);
    }

    [Fact]
    public async Task ComputeAsync_FailuresByCategory_AggregatesCounts()
    {
        var fixture = Build();
        var task = AddTask(fixture.Tasks, AgentTaskStatus.Completed);

        fixture.Failures.Failures.Add(new ExecutionFailure(task.Id, null, FailureCategory.BuildFailure, "BackendDeveloper", "msg1"));
        fixture.Failures.Failures.Add(new ExecutionFailure(task.Id, null, FailureCategory.BuildFailure, "BackendDeveloper", "msg2"));
        fixture.Failures.Failures.Add(new ExecutionFailure(task.Id, null, FailureCategory.QaRejection, "QaEngineer", "msg3"));

        var snapshot = await fixture.Calculator.ComputeAsync();

        Assert.Equal(2, snapshot.FailuresByCategory[FailureCategory.BuildFailure]);
        Assert.Equal(1, snapshot.FailuresByCategory[FailureCategory.QaRejection]);
    }
}
