using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Publishing;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.GitHub.Package;

/// <summary>
/// Coordinates the package publishing pipeline:
/// validate merged + released task → create isolated package worktree at exact MergeCommitSha
/// → dotnet pack → validate version + metadata → persist.
/// On human /package approve: validate → dotnet nuget push → persist.
/// Never publishes automatically. /package approve is the only publish path.
/// Never packs from the original repository — always from an isolated worktree.
/// </summary>
public sealed class PackageOrchestrator : IPackageOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProjectRegistry _projectRegistry;
    private readonly IPackagePublisher _packagePublisher;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<PackageOrchestrator> _logger;

    public PackageOrchestrator(
        IServiceScopeFactory scopeFactory,
        IProjectRegistry projectRegistry,
        IPackagePublisher packagePublisher,
        IWorkspaceManager workspaceManager,
        ILogger<PackageOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _projectRegistry = projectRegistry;
        _packagePublisher = packagePublisher;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task<PackageOrchestratorResult> PrepareAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        AgentTask? task;
        Core.Domain.Package? existingPackage;
        Core.Domain.Release? release;
        AgentExecutionRecord? qaExecution;
        AgentExecutionRecord? reviewerExecution;

        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            task = await taskService.GetTaskAsync(taskId, cancellationToken);
            var packageRepo = scope.ServiceProvider.GetRequiredService<IPackageRepository>();
            existingPackage = await packageRepo.GetByTaskIdAsync(taskId, cancellationToken);
            var releaseRepo = scope.ServiceProvider.GetRequiredService<IReleaseRepository>();
            release = await releaseRepo.GetByTaskIdAsync(taskId, cancellationToken);
        }

        if (task is null)
            return Fail("Task not found.");

        // Idempotent: already prepared or published
        if (existingPackage is not null && existingPackage.Status is not PackageStatus.Failed)
        {
            _logger.LogInformation("Package already prepared for task {TaskId}: {Id} {Version} ({Status})",
                taskId, existingPackage.PackageId, existingPackage.PackageVersion, existingPackage.Status);
            return ToResult(existingPackage, $"Package already prepared: {existingPackage.PackageId} {existingPackage.PackageVersion}");
        }

        if (task.Status is not (AgentTaskStatus.AwaitingReview or AgentTaskStatus.Completed))
            return Fail($"Task is in status '{task.Status}'. Only AwaitingReview or Completed tasks can be packaged.");

        if (string.IsNullOrWhiteSpace(task.MergeCommitSha))
            return Fail("Task has not been merged. Run /merge first.");

        if (release is null)
            return Fail("No GitHub release found for this task. Run /release first.");

        if (release.Status != ReleaseStatus.Published)
            return Fail($"GitHub release is in status '{release.Status}'. Run /release {taskId.ToString("N")[..8]} approve first.");

        if (string.IsNullOrWhiteSpace(release.Version))
            return Fail("GitHub release has no version set. Cannot determine package version.");

        var project = _projectRegistry.Find(task.ProjectId);
        if (project is null)
            return Fail($"Project '{task.ProjectId}' is not registered.");

        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
            qaExecution = await repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.QaEngineer, cancellationToken);
            reviewerExecution = await repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.CodeReviewer, cancellationToken);
        }

        if (qaExecution is null || !FindingsContain(qaExecution.Findings, "QA_PASSED"))
            return Fail("QA did not pass. Cannot prepare package.");

        if (reviewerExecution is null || !FindingsContain(reviewerExecution.Findings, "REVIEW_APPROVED"))
            return Fail("Code review was not approved. Cannot prepare package.");

        // Create an isolated git worktree at the exact merged commit so that:
        // - stale local main does not affect the build
        // - the original repository is never modified or built from
        string workspacePath;
        try
        {
            var workspace = await _workspaceManager.CreateForPackagingAsync(
                project, taskId, task.MergeCommitSha!, cancellationToken);
            workspacePath = workspace.WorkspacePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create package workspace for task {TaskId}", taskId);
            return Fail($"Failed to create isolated package workspace: {ex.Message}");
        }

        _logger.LogInformation("Package workspace created at {WorkspacePath} for commit {Sha}",
            workspacePath, task.MergeCommitSha);

        var outputDir = Path.Combine(workspacePath, "artifacts", "packages");

        var projectFilePath = project.PackageProjectPath is not null
            ? Path.Combine(workspacePath, project.PackageProjectPath)
            : null;

        var prepareResult = await _packagePublisher.PrepareAsync(new PackagePrepareRequest
        {
            RepositoryPath = workspacePath,
            ProjectFilePath = projectFilePath,
            Configuration = "Release",
            OutputDirectory = outputDir,
            PackageVersion = release.Version,
            ExpectedVersion = release.Version,
            ExpectedPackageId = project.NuGetPackageId
        }, cancellationToken);

        if (!prepareResult.Succeeded)
        {
            _logger.LogError("Package preparation failed for task {TaskId}: {Error}", taskId, prepareResult.ErrorMessage);
            return Fail($"Package preparation failed: {prepareResult.ErrorMessage}");
        }

        if (!string.Equals(prepareResult.PackageVersion, release.Version, StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"Package version mismatch: built '{prepareResult.PackageVersion}' but release version is '{release.Version}'. " +
                        "Ensure the project version matches the GitHub release version.");
        }

        var package = new Core.Domain.Package(
            task.Id,
            prepareResult.PackageId!,
            prepareResult.PackageVersion!,
            prepareResult.PackagePath!);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var packageRepo = scope.ServiceProvider.GetRequiredService<IPackageRepository>();
            await packageRepo.AddAsync(package, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist package record for task {TaskId}", taskId);
        }

        _logger.LogInformation("Package prepared for task {TaskId}: {Id} {Version}", taskId, package.PackageId, package.PackageVersion);

        return ToResult(package,
            $"Package prepared: {package.PackageId} {package.PackageVersion}\n" +
            $"Path: {package.PackagePath}\n" +
            $"Run /package {taskId.ToString("N")[..8]} approve to publish to NuGet.");
    }

    public async Task<PackageOrchestratorResult> ApproveAndPublishAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        AgentTask? task;
        Core.Domain.Package? package;

        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            task = await taskService.GetTaskAsync(taskId, cancellationToken);
            var packageRepo = scope.ServiceProvider.GetRequiredService<IPackageRepository>();
            package = await packageRepo.GetByTaskIdAsync(taskId, cancellationToken);
        }

        if (task is null)
            return Fail("Task not found.");

        if (package is null)
            return Fail("No package prepared for this task. Run /package first.");

        // Idempotent: already published
        if (package.Status == PackageStatus.Published)
        {
            _logger.LogInformation("Package already published for task {TaskId}: {Id} {Version}", taskId, package.PackageId, package.PackageVersion);
            return ToResult(package, $"Package already published: {package.PackageId} {package.PackageVersion}");
        }

        if (package.Status == PackageStatus.Failed)
            return Fail("Previous package attempt failed. Run /package to prepare a new package.");

        if (!File.Exists(package.PackagePath))
            return Fail($"Package file not found at '{package.PackagePath}'. Run /package to re-prepare.");

        var publishResult = await _packagePublisher.PublishAsync(new PackagePublishRequest
        {
            PackagePath = package.PackagePath
        }, cancellationToken);

        if (!publishResult.Succeeded)
        {
            _logger.LogError("Package publish failed for task {TaskId}: {Error}", taskId, publishResult.ErrorMessage);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var packageRepo = scope.ServiceProvider.GetRequiredService<IPackageRepository>();
                package.SetFailed();
                await packageRepo.UpdateAsync(package, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist package failure for task {TaskId}", taskId);
            }
            return Fail($"Package publish failed: {publishResult.ErrorMessage}");
        }

        package.SetPublished();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var packageRepo = scope.ServiceProvider.GetRequiredService<IPackageRepository>();
            await packageRepo.UpdateAsync(package, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist published package for task {TaskId}", taskId);
        }

        _logger.LogInformation("Package published for task {TaskId}: {Id} {Version}", taskId, package.PackageId, package.PackageVersion);

        return ToResult(package, $"Package published: {package.PackageId} {package.PackageVersion}");
    }

    public async Task<PackageOrchestratorResult> GetInfoAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        Core.Domain.Package? package;

        using (var scope = _scopeFactory.CreateScope())
        {
            var packageRepo = scope.ServiceProvider.GetRequiredService<IPackageRepository>();
            package = await packageRepo.GetByTaskIdAsync(taskId, cancellationToken);
        }

        if (package is null)
            return Fail("No package found for this task. Run /package to prepare one.");

        return ToResult(package, $"Package: {package.PackageId} {package.PackageVersion} — {package.Status}");
    }

    private static bool FindingsContain(string? findings, string marker) =>
        findings is not null && findings.Contains(marker, StringComparison.OrdinalIgnoreCase);

    private static PackageOrchestratorResult ToResult(Core.Domain.Package package, string summary) => new()
    {
        Succeeded = true,
        PackageId = package.PackageId,
        PackageVersion = package.PackageVersion,
        PackagePath = package.PackagePath,
        Status = package.Status,
        PreparedAt = package.PreparedAt,
        PublishedAt = package.PublishedAt,
        Summary = summary
    };

    private static PackageOrchestratorResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error,
        Summary = error
    };
}
