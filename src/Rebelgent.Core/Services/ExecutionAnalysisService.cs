using System.Security.Cryptography;
using System.Text;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Observability;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Core.Services;

/// <inheritdoc cref="IExecutionAnalysisService"/>
public sealed class ExecutionAnalysisService : IExecutionAnalysisService
{
    // Bounded "get everything" scan — Rebelgent's task volume is small (single-node, self-hosted),
    // so a fixed high ceiling stands in for a true GetAllAsync on IAgentTaskRepository.
    private const int MaxTasksScanned = 10_000;

    private readonly IAgentTaskRepository _taskRepository;
    private readonly IAgentExecutionRepository _executionRepository;
    private readonly IReleaseRepository _releaseRepository;
    private readonly IPackageRepository _packageRepository;
    private readonly IExecutionFailureRepository _failureRepository;
    private readonly IObservabilityExporter _observabilityExporter;

    public ExecutionAnalysisService(
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

    public async Task<int> CategorizeAndPersistFailuresAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project ID cannot be empty.", nameof(projectId));

        var persisted = 0;

        var allTasks = await _taskRepository.GetRecentAsync(MaxTasksScanned, cancellationToken);
        var tasks = allTasks.Where(t => string.Equals(t.ProjectId, projectId, StringComparison.OrdinalIgnoreCase)).ToList();
        var projectTaskIds = tasks.Select(t => t.Id).ToHashSet();

        foreach (var task in tasks)
        {
            var executions = await _executionRepository.GetAllByTaskIdAsync(task.Id, cancellationToken);
            foreach (var execution in executions)
            {
                if (execution.Status is not (ExecutionStatus.Failed or ExecutionStatus.TimedOut) &&
                    !IsSoftRejection(execution))
                    continue;

                if (await _failureRepository.ExistsForExecutionAsync(execution.Id, cancellationToken))
                    continue;

                var (category, message) = Categorize(execution);
                var failure = new ExecutionFailure(task.Id, execution.Id, category, execution.Role.ToString(), message);
                await _failureRepository.AddAsync(failure, cancellationToken);
                await _observabilityExporter.ExportFailureAsync(failure, cancellationToken);
                persisted++;
            }
        }

        var releases = await _releaseRepository.GetAllAsync(cancellationToken);
        foreach (var release in releases.Where(r => r.Status == ReleaseStatus.Failed && projectTaskIds.Contains(r.TaskId)))
        {
            if (await _failureRepository.ExistsForExecutionAsync(release.Id, cancellationToken))
                continue;

            var failure = new ExecutionFailure(
                release.TaskId, release.Id, FailureCategory.ReleaseFailure, "ReleaseManager",
                $"Release {release.TagName} failed to publish.");
            await _failureRepository.AddAsync(failure, cancellationToken);
            await _observabilityExporter.ExportFailureAsync(failure, cancellationToken);
            persisted++;
        }

        var packages = await _packageRepository.GetAllAsync(cancellationToken);
        foreach (var package in packages.Where(p => p.Status == PackageStatus.Failed && projectTaskIds.Contains(p.TaskId)))
        {
            if (await _failureRepository.ExistsForExecutionAsync(package.Id, cancellationToken))
                continue;

            var failure = new ExecutionFailure(
                package.TaskId, package.Id, FailureCategory.PackageFailure, "PackagePublisher",
                $"Package {package.PackageId} {package.PackageVersion} failed to publish.");
            await _failureRepository.AddAsync(failure, cancellationToken);
            await _observabilityExporter.ExportFailureAsync(failure, cancellationToken);
            persisted++;
        }

        return persisted;
    }

    public async Task<IReadOnlyList<DetectedPattern>> AnalyzeAsync(string projectId, int minOccurrences = 2, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project ID cannot be empty.", nameof(projectId));

        await CategorizeAndPersistFailuresAsync(projectId, cancellationToken);

        var allTasks = await _taskRepository.GetRecentAsync(MaxTasksScanned, cancellationToken);
        var projectTaskIds = allTasks
            .Where(t => string.Equals(t.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id)
            .ToHashSet();

        var allFailures = await _failureRepository.GetAllAsync(cancellationToken);
        var failures = allFailures.Where(f => projectTaskIds.Contains(f.TaskId)).ToList();

        var patterns = failures
            .GroupBy(f => (f.Category, f.Source))
            .Where(g => g.Count() >= minOccurrences)
            .Select(g =>
            {
                var category = g.Key.Category;
                var source = g.Key.Source;
                var occurrences = g.Count();
                var samples = g.OrderByDescending(f => f.DetectedAt).Take(5).ToList();

                var title = BuildTitle(category, source, occurrences);
                var targetArea = BuildTargetArea(category, source);
                var evidence = BuildEvidence(source, category, samples);
                var fingerprint = ComputeFingerprint(projectId, category, targetArea);

                return new DetectedPattern(title, evidence, category, source, targetArea, occurrences, fingerprint);
            })
            .OrderByDescending(p => p.Occurrences)
            .ToList();

        return patterns;
    }

    // Some executions record a rejection via Findings text rather than a Failed status
    // (QA/Reviewer complete "successfully" — they ran to completion — but the verdict is negative).
    private static bool IsSoftRejection(AgentExecutionRecord execution)
    {
        if (execution.Status != ExecutionStatus.Succeeded)
            return false;

        return execution.Role switch
        {
            AgentRole.QaEngineer => !FindingsContain(execution.Findings, "QA_PASSED"),
            AgentRole.CodeReviewer => !FindingsContain(execution.Findings, "REVIEW_APPROVED"),
            _ => false
        };
    }

    private static (FailureCategory Category, string Message) Categorize(AgentExecutionRecord execution)
    {
        if (execution.Status == ExecutionStatus.TimedOut)
            return (FailureCategory.Timeout, execution.ErrorMessage ?? $"{execution.Role} execution timed out.");

        if (execution.Role == AgentRole.QaEngineer && IsSoftRejection(execution))
            return (FailureCategory.QaRejection, ExtractSnippet(execution.Findings) ?? "QA did not pass.");

        if (execution.Role == AgentRole.CodeReviewer && IsSoftRejection(execution))
            return (FailureCategory.ReviewerRejection, ExtractSnippet(execution.Findings) ?? "Code review was not approved.");

        if (execution.Role == AgentRole.BackendDeveloper || execution.Role == AgentRole.FrontendDeveloper)
        {
            if (execution.BuildSucceeded == false)
                return (FailureCategory.BuildFailure, execution.ErrorMessage ?? "Build failed.");
            if (execution.TestsSucceeded == false)
                return (FailureCategory.TestFailure, execution.ErrorMessage ?? "Tests failed.");
        }

        var error = execution.ErrorMessage;
        if (!string.IsNullOrWhiteSpace(error))
        {
            if (ContainsAny(error, "workspace"))
                return (FailureCategory.WorkspaceFailure, error);
            if (ContainsAny(error, "worktree", "git "))
                return (FailureCategory.GitFailure, error);
            if (ContainsAny(error, "gh ", "github", "pull request"))
                return (FailureCategory.GitHubFailure, error);
            if (execution.Status == ExecutionStatus.Failed)
                return (FailureCategory.ProcessFailure, error);
        }

        if (execution.Status == ExecutionStatus.Failed)
            return (FailureCategory.AgentFailure, error ?? $"{execution.Role} execution failed.");

        return (FailureCategory.Unknown, error ?? "Unrecognized failure.");
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static bool FindingsContain(string? findings, string marker) =>
        findings is not null && findings.Contains(marker, StringComparison.OrdinalIgnoreCase);

    private static string? ExtractSnippet(string? findings)
    {
        if (string.IsNullOrWhiteSpace(findings)) return null;
        var trimmed = findings.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "...";
    }

    private static string BuildTitle(FailureCategory category, string source, int occurrences) => category switch
    {
        FailureCategory.BuildFailure => $"{source} failed the build {occurrences} times",
        FailureCategory.TestFailure => $"{source} failed tests {occurrences} times",
        FailureCategory.QaRejection => $"QA repeatedly rejects {source} work ({occurrences} occurrences)",
        FailureCategory.ReviewerRejection => $"Code review repeatedly rejects {source} work ({occurrences} occurrences)",
        FailureCategory.ReleaseFailure => $"Release publishing repeatedly fails ({occurrences} occurrences)",
        FailureCategory.PackageFailure => $"Package publishing repeatedly fails ({occurrences} occurrences)",
        FailureCategory.Timeout => $"{source} repeatedly times out ({occurrences} occurrences)",
        FailureCategory.WorkspaceFailure => $"{source} repeatedly hits workspace failures ({occurrences} occurrences)",
        FailureCategory.GitFailure => $"{source} repeatedly hits git failures ({occurrences} occurrences)",
        FailureCategory.GitHubFailure => $"{source} repeatedly hits GitHub failures ({occurrences} occurrences)",
        FailureCategory.ProcessFailure => $"{source} repeatedly fails to complete ({occurrences} occurrences)",
        FailureCategory.AgentFailure => $"{source} agent repeatedly fails ({occurrences} occurrences)",
        _ => $"{source} repeatedly hits uncategorized failures ({occurrences} occurrences)"
    };

    private static string BuildTargetArea(FailureCategory category, string source) => category switch
    {
        FailureCategory.BuildFailure or FailureCategory.TestFailure => $"{source} Prompt / Workflow",
        FailureCategory.QaRejection => "QA Checklist",
        FailureCategory.ReviewerRejection => "Code Reviewer Prompt / Architecture Guidance",
        FailureCategory.ReleaseFailure => "Release Pipeline Configuration",
        FailureCategory.PackageFailure => "Package Publishing Configuration",
        FailureCategory.WorkspaceFailure or FailureCategory.GitFailure => "Workspace / Git Safety Checks",
        FailureCategory.GitHubFailure => "GitHub Integration",
        FailureCategory.Timeout => $"{source} Timeout Handling",
        _ => $"{source} Agent Workflow"
    };

    private static string BuildEvidence(string source, FailureCategory category, IReadOnlyList<ExecutionFailure> samples)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Category: {category}");
        sb.AppendLine($"Source: {source}");
        sb.AppendLine($"Recent examples ({samples.Count} of the most recent occurrences):");
        foreach (var s in samples)
            sb.AppendLine($"- [{s.DetectedAt:yyyy-MM-dd HH:mm} UTC] {s.Message}");
        return sb.ToString().TrimEnd();
    }

    private static string ComputeFingerprint(string projectId, FailureCategory category, string targetArea)
    {
        // Fingerprint is over project + category + target area only (not the full evidence text,
        // which changes as new sample failures roll in) so the same unresolved issue in the same
        // project keeps matching its existing proposal instead of spawning a duplicate on every
        // analysis run, while the same category/area in a different project fingerprints separately.
        var normalized = $"{projectId}|{category}|{targetArea}".Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }
}
