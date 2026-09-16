using Rebelgent.Core.Domain;
using Rebelgent.Core.Observability;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Core.Services;

/// <inheritdoc cref="IMetricsCalculator"/>
public sealed class MetricsCalculator : IMetricsCalculator
{
    private const int MaxTasksScanned = 10_000;

    private readonly IAgentTaskRepository _taskRepository;
    private readonly IAgentExecutionRepository _executionRepository;
    private readonly IReleaseRepository _releaseRepository;
    private readonly IPackageRepository _packageRepository;
    private readonly IExecutionFailureRepository _failureRepository;
    private readonly IObservabilityExporter _observabilityExporter;

    public MetricsCalculator(
        IAgentTaskRepository taskRepository,
        IAgentExecutionRepository executionRepository,
        IReleaseRepository releaseRepository,
        IPackageRepository packageRepository,
        IExecutionFailureRepository failureRepository,
        IObservabilityExporter? observabilityExporter = null)
    {
        _taskRepository = taskRepository;
        _executionRepository = executionRepository;
        _releaseRepository = releaseRepository;
        _packageRepository = packageRepository;
        _failureRepository = failureRepository;
        _observabilityExporter = observabilityExporter ?? new NullObservabilityExporter();
    }

    public async Task<AgentMetricsSnapshot> ComputeAsync(CancellationToken cancellationToken = default)
    {
        var tasks = await _taskRepository.GetRecentAsync(MaxTasksScanned, cancellationToken);

        var terminalTasks = tasks.Where(t => t.Status is AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled).ToList();
        var taskSuccessRate = Rate(terminalTasks.Count(t => t.Status == AgentTaskStatus.Completed), terminalTasks.Count);

        var developerTotal = 0;
        var developerFailed = 0;
        var qaTotal = 0;
        var qaPassed = 0;
        var reviewTotal = 0;
        var reviewApproved = 0;
        var retrySum = 0;
        var tasksWithDeveloperExecution = 0;

        foreach (var task in tasks)
        {
            var executions = await _executionRepository.GetAllByTaskIdAsync(task.Id, cancellationToken);

            var developerExecutions = executions.Where(e => e.Role is AgentRole.BackendDeveloper or AgentRole.FrontendDeveloper).ToList();
            if (developerExecutions.Count > 0)
            {
                tasksWithDeveloperExecution++;
                retrySum += Math.Max(0, developerExecutions.Count - 1);
            }

            foreach (var e in developerExecutions)
            {
                developerTotal++;
                if (e.Status is ExecutionStatus.Failed or ExecutionStatus.TimedOut || e.BuildSucceeded == false || e.TestsSucceeded == false)
                    developerFailed++;
            }

            foreach (var e in executions.Where(e => e.Role == AgentRole.QaEngineer && e.Status is ExecutionStatus.Succeeded or ExecutionStatus.Failed))
            {
                qaTotal++;
                if (e.Status == ExecutionStatus.Succeeded && FindingsContain(e.Findings, "QA_PASSED"))
                    qaPassed++;
            }

            foreach (var e in executions.Where(e => e.Role == AgentRole.CodeReviewer && e.Status is ExecutionStatus.Succeeded or ExecutionStatus.Failed))
            {
                reviewTotal++;
                if (e.Status == ExecutionStatus.Succeeded && FindingsContain(e.Findings, "REVIEW_APPROVED"))
                    reviewApproved++;
            }
        }

        var releases = await _releaseRepository.GetAllAsync(cancellationToken);
        var packages = await _packageRepository.GetAllAsync(cancellationToken);

        var failures = await _failureRepository.GetAllAsync(cancellationToken);
        var failuresByCategory = failures
            .GroupBy(f => f.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        var snapshot = new AgentMetricsSnapshot(
            totalTasks: tasks.Count,
            taskSuccessRate: taskSuccessRate,
            developerFailureRate: Rate(developerFailed, developerTotal),
            qaPassRate: Rate(qaPassed, qaTotal),
            reviewApprovalRate: Rate(reviewApproved, reviewTotal),
            averageRetriesPerTask: tasksWithDeveloperExecution == 0 ? 0d : retrySum / (double)tasksWithDeveloperExecution,
            releaseFailureRate: Rate(releases.Count(r => r.Status == ReleaseStatus.Failed), releases.Count),
            packageFailureRate: Rate(packages.Count(p => p.Status == PackageStatus.Failed), packages.Count),
            failuresByCategory: failuresByCategory);

        await _observabilityExporter.ExportMetricsAsync(snapshot, cancellationToken);
        return snapshot;
    }

    private static double Rate(int numerator, int denominator) =>
        denominator == 0 ? 0d : numerator / (double)denominator;

    private static bool FindingsContain(string? findings, string marker) =>
        findings is not null && findings.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
