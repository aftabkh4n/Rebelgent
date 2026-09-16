using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Core.Tests.Services.Fakes;

namespace Rebelgent.Core.Tests.Services;

public class ExecutionAnalysisServiceTests
{
    private static (ExecutionAnalysisService Service, FakeAgentTaskRepository Tasks, FakeAgentExecutionRepository Executions,
        FakeReleaseRepository Releases, FakePackageRepository Packages, FakeExecutionFailureRepository Failures) Build()
    {
        var tasks = new FakeAgentTaskRepository();
        var executions = new FakeAgentExecutionRepository();
        var releases = new FakeReleaseRepository();
        var packages = new FakePackageRepository();
        var failures = new FakeExecutionFailureRepository();
        var service = new ExecutionAnalysisService(tasks, executions, releases, packages, failures);
        return (service, tasks, executions, releases, packages, failures);
    }

    private static AgentTask AddTask((ExecutionAnalysisService Service, FakeAgentTaskRepository Tasks, FakeAgentExecutionRepository Executions,
        FakeReleaseRepository Releases, FakePackageRepository Packages, FakeExecutionFailureRepository Failures) fixture)
    {
        var task = new AgentTask("sandbox", "Test task", "desc", AgentRole.BackendDeveloper);
        fixture.Tasks.Tasks.Add(task);
        return task;
    }

    private static AgentExecutionRecord AddExecution(
        (ExecutionAnalysisService Service, FakeAgentTaskRepository Tasks, FakeAgentExecutionRepository Executions,
            FakeReleaseRepository Releases, FakePackageRepository Packages, FakeExecutionFailureRepository Failures) fixture,
        Guid taskId, AgentRole role)
    {
        var execution = new AgentExecutionRecord(taskId, "sandbox", @"D:\ws", "rebelgent/task-x", role);
        execution.MarkRunning();
        fixture.Executions.Executions.Add(execution);
        return execution;
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_BuildFailure_CategorizedAsBuildFailure()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var execution = AddExecution(fixture, task.Id, AgentRole.BackendDeveloper);
        execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);

        await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        var failure = Assert.Single(fixture.Failures.Failures);
        Assert.Equal(FailureCategory.BuildFailure, failure.Category);
        Assert.Equal(execution.Id, failure.ExecutionId);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_TestFailureWithSuccessfulBuild_CategorizedAsTestFailure()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var execution = AddExecution(fixture, task.Id, AgentRole.BackendDeveloper);
        execution.Complete("output", "build ok", "test failed", buildSucceeded: true, testsSucceeded: false);

        await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        var failure = Assert.Single(fixture.Failures.Failures);
        Assert.Equal(FailureCategory.TestFailure, failure.Category);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_TimedOutExecution_CategorizedAsTimeout()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var execution = AddExecution(fixture, task.Id, AgentRole.BackendDeveloper);
        execution.MarkTimedOut("Execution exceeded the configured timeout.");

        await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        var failure = Assert.Single(fixture.Failures.Failures);
        Assert.Equal(FailureCategory.Timeout, failure.Category);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_QaSucceededWithoutPassMarker_CategorizedAsQaRejection()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var execution = AddExecution(fixture, task.Id, AgentRole.QaEngineer);
        execution.CompleteWithFindings("output", "QA_FAILED: missing input validation on the login endpoint.");

        await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        var failure = Assert.Single(fixture.Failures.Failures);
        Assert.Equal(FailureCategory.QaRejection, failure.Category);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_QaSucceededWithPassMarker_NotCategorizedAsFailure()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var execution = AddExecution(fixture, task.Id, AgentRole.QaEngineer);
        execution.CompleteWithFindings("output", "QA_PASSED: all checks green.");

        await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        Assert.Empty(fixture.Failures.Failures);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_ReviewerSucceededWithoutApprovalMarker_CategorizedAsReviewerRejection()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var execution = AddExecution(fixture, task.Id, AgentRole.CodeReviewer);
        execution.CompleteWithFindings("output", "REVIEW_CHANGES_REQUESTED: extract the duplicated validation logic.");

        await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        var failure = Assert.Single(fixture.Failures.Failures);
        Assert.Equal(FailureCategory.ReviewerRejection, failure.Category);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_FailedReleaseRecord_CategorizedAsReleaseFailure()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var release = new Release(task.Id, "1.0.0", "Release 1.0.0", "notes", false, new string('a', 40));
        release.SetFailed();
        fixture.Releases.Releases.Add(release);

        await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        var failure = Assert.Single(fixture.Failures.Failures);
        Assert.Equal(FailureCategory.ReleaseFailure, failure.Category);
        Assert.Equal(release.Id, failure.ExecutionId);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_FailedPackageRecord_CategorizedAsPackageFailure()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var package = new Package(task.Id, "MyLib", "1.0.0", @"C:\out\MyLib.1.0.0.nupkg");
        package.SetFailed();
        fixture.Packages.Packages.Add(package);

        await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        var failure = Assert.Single(fixture.Failures.Failures);
        Assert.Equal(FailureCategory.PackageFailure, failure.Category);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_CalledTwice_DoesNotDuplicate()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var execution = AddExecution(fixture, task.Id, AgentRole.BackendDeveloper);
        execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);

        await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");
        var persistedSecondRun = await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        Assert.Single(fixture.Failures.Failures);
        Assert.Equal(0, persistedSecondRun);
    }

    [Fact]
    public async Task AnalyzeAsync_BelowMinOccurrences_NoPatternDetected()
    {
        var fixture = Build();
        var task = AddTask(fixture);
        var execution = AddExecution(fixture, task.Id, AgentRole.BackendDeveloper);
        execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);

        var patterns = await fixture.Service.AnalyzeAsync("sandbox", minOccurrences: 2);

        Assert.Empty(patterns);
    }

    [Fact]
    public async Task AnalyzeAsync_AtMinOccurrences_DetectsPattern()
    {
        var fixture = Build();
        for (var i = 0; i < 3; i++)
        {
            var task = AddTask(fixture);
            var execution = AddExecution(fixture, task.Id, AgentRole.BackendDeveloper);
            execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);
        }

        var patterns = await fixture.Service.AnalyzeAsync("sandbox", minOccurrences: 2);

        var pattern = Assert.Single(patterns);
        Assert.Equal(FailureCategory.BuildFailure, pattern.Category);
        Assert.Equal(3, pattern.Occurrences);
        Assert.Contains("failed the build", pattern.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_SamePatternAnalyzedTwice_ProducesSameFingerprint()
    {
        var fixture = Build();
        for (var i = 0; i < 2; i++)
        {
            var task = AddTask(fixture);
            var execution = AddExecution(fixture, task.Id, AgentRole.BackendDeveloper);
            execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);
        }

        var firstRun = await fixture.Service.AnalyzeAsync("sandbox", minOccurrences: 2);
        var secondRun = await fixture.Service.AnalyzeAsync("sandbox", minOccurrences: 2);

        Assert.Equal(firstRun.Single().EvidenceFingerprint, secondRun.Single().EvidenceFingerprint);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_ExportsEachFailureToObservabilityExporter()
    {
        var tasks = new FakeAgentTaskRepository();
        var executions = new FakeAgentExecutionRepository();
        var releases = new FakeReleaseRepository();
        var packages = new FakePackageRepository();
        var failures = new FakeExecutionFailureRepository();
        var exporter = new FakeObservabilityExporter();
        var service = new ExecutionAnalysisService(tasks, executions, releases, packages, failures, exporter);

        var task = new AgentTask("sandbox", "Test task", "desc", AgentRole.BackendDeveloper);
        tasks.Tasks.Add(task);
        var execution = new AgentExecutionRecord(task.Id, "sandbox", @"D:\ws", "rebelgent/task-x", AgentRole.BackendDeveloper);
        execution.MarkRunning();
        execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);
        executions.Executions.Add(execution);

        await service.CategorizeAndPersistFailuresAsync("sandbox");

        Assert.Single(exporter.ExportedFailures);
    }

    [Fact]
    public async Task CategorizeAndPersistFailuresAsync_TaskInDifferentProject_IsIgnored()
    {
        var fixture = Build();
        var otherProjectTask = new AgentTask("other-project", "Test task", "desc", AgentRole.BackendDeveloper);
        fixture.Tasks.Tasks.Add(otherProjectTask);
        var execution = AddExecution(fixture, otherProjectTask.Id, AgentRole.BackendDeveloper);
        execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);

        var persisted = await fixture.Service.CategorizeAndPersistFailuresAsync("sandbox");

        Assert.Equal(0, persisted);
        Assert.Empty(fixture.Failures.Failures);
    }

    [Fact]
    public async Task AnalyzeAsync_PatternInDifferentProject_IsNotDetected()
    {
        var fixture = Build();
        for (var i = 0; i < 3; i++)
        {
            var otherProjectTask = new AgentTask("other-project", "Test task", "desc", AgentRole.BackendDeveloper);
            fixture.Tasks.Tasks.Add(otherProjectTask);
            var execution = AddExecution(fixture, otherProjectTask.Id, AgentRole.BackendDeveloper);
            execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);
        }

        var patterns = await fixture.Service.AnalyzeAsync("sandbox", minOccurrences: 2);

        Assert.Empty(patterns);
    }

    [Fact]
    public async Task AnalyzeAsync_SameCategoryDifferentProjects_ProduceDifferentFingerprints()
    {
        var fixture = Build();
        for (var i = 0; i < 2; i++)
        {
            var task = AddTask(fixture);
            var execution = AddExecution(fixture, task.Id, AgentRole.BackendDeveloper);
            execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);
        }

        for (var i = 0; i < 2; i++)
        {
            var otherProjectTask = new AgentTask("other-project", "Test task", "desc", AgentRole.BackendDeveloper);
            fixture.Tasks.Tasks.Add(otherProjectTask);
            var execution = AddExecution(fixture, otherProjectTask.Id, AgentRole.BackendDeveloper);
            execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);
        }

        var sandboxPatterns = await fixture.Service.AnalyzeAsync("sandbox", minOccurrences: 2);
        var otherPatterns = await fixture.Service.AnalyzeAsync("other-project", minOccurrences: 2);

        Assert.NotEqual(sandboxPatterns.Single().EvidenceFingerprint, otherPatterns.Single().EvidenceFingerprint);
    }

    [Fact]
    public async Task AnalyzeAsync_DifferentCategories_KeptSeparate()
    {
        var fixture = Build();
        for (var i = 0; i < 2; i++)
        {
            var task = AddTask(fixture);
            var execution = AddExecution(fixture, task.Id, AgentRole.BackendDeveloper);
            execution.Complete("output", "build error", "not run", buildSucceeded: false, testsSucceeded: false);
        }

        for (var i = 0; i < 2; i++)
        {
            var task = AddTask(fixture);
            var execution = AddExecution(fixture, task.Id, AgentRole.QaEngineer);
            execution.CompleteWithFindings("output", "QA_FAILED: missing validation.");
        }

        var patterns = await fixture.Service.AnalyzeAsync("sandbox", minOccurrences: 2);

        Assert.Equal(2, patterns.Count);
        Assert.Contains(patterns, p => p.Category == FailureCategory.BuildFailure);
        Assert.Contains(patterns, p => p.Category == FailureCategory.QaRejection);
    }
}
