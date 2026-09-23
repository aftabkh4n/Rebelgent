using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebelgent.ClaudeCode.ReleaseNotes;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Projects;

namespace Rebelgent.GitHub.Release;

/// <summary>
/// Coordinates the release pipeline: validate merged task → run Release Manager agent
/// → persist release record. On human approval: validate → create GitHub Release → persist.
/// Never creates tags or releases before explicit human /release approve command.
/// </summary>
public sealed class ReleaseOrchestrator : IReleaseOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProjectRegistry _projectRegistry;
    private readonly IReleaseNotesAgent _releaseNotesAgent;
    private readonly IReleaseService _releaseService;
    private readonly ILogger<ReleaseOrchestrator> _logger;

    public ReleaseOrchestrator(
        IServiceScopeFactory scopeFactory,
        IProjectRegistry projectRegistry,
        IReleaseNotesAgent releaseNotesAgent,
        IReleaseService releaseService,
        ILogger<ReleaseOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _projectRegistry = projectRegistry;
        _releaseNotesAgent = releaseNotesAgent;
        _releaseService = releaseService;
        _logger = logger;
    }

    public async Task<ReleaseOrchestratorResult> PrepareAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        AgentTask? task;
        Core.Domain.Release? existingRelease;
        AgentExecutionRecord? qaExecution;
        AgentExecutionRecord? reviewerExecution;

        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            task = await taskService.GetTaskAsync(taskId, cancellationToken);
            var releaseRepo = scope.ServiceProvider.GetRequiredService<IReleaseRepository>();
            existingRelease = await releaseRepo.GetByTaskIdAsync(taskId, cancellationToken);
        }

        if (task is null)
            return Fail("Task not found.");

        // Idempotent: already prepared or approved
        if (existingRelease is not null && existingRelease.Status is not ReleaseStatus.Failed)
        {
            _logger.LogInformation("Release already exists for task {TaskId}: {Version} ({Status})", taskId, existingRelease.Version, existingRelease.Status);
            return ToResult(existingRelease, $"Release already prepared: {existingRelease.TagName} — {existingRelease.Title}");
        }

        if (string.IsNullOrWhiteSpace(task.MergeCommitSha))
            return Fail("Task has not been merged. Run /merge first.");

        if (task.Status is not (AgentTaskStatus.AwaitingReview or AgentTaskStatus.Completed))
            return Fail($"Task is in status '{task.Status}'. Only AwaitingReview or Completed tasks can be released.");

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
            return Fail("QA did not pass. Cannot prepare release.");

        if (reviewerExecution is null || !FindingsContain(reviewerExecution.Findings, "REVIEW_APPROVED"))
            return Fail("Code review was not approved. Cannot prepare release.");

        // Query the latest published release to inform the agent's version suggestion.
        // A null result means no releases exist yet (first release) — not a failure.
        string? latestVersion = null;
        try
        {
            latestVersion = await _releaseService.GetLatestReleaseVersionAsync(
                project.RepositoryPath, project.GitHubRepository, cancellationToken);

            if (latestVersion is not null)
                _logger.LogInformation("Latest release for {ProjectId}: {Version}", task.ProjectId, latestVersion);
            else
                _logger.LogInformation("No existing releases found for {ProjectId}", task.ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not query latest release for {ProjectId}; proceeding without version context", task.ProjectId);
        }

        var agentOutput = await _releaseNotesAgent.PrepareAsync(new ReleaseNotesInput
        {
            TaskTitle = task.Title,
            TaskDescription = task.Description,
            MergeCommitSha = task.MergeCommitSha,
            QaFindings = qaExecution.Findings ?? string.Empty,
            ReviewerFindings = reviewerExecution.Findings ?? string.Empty,
            ProjectId = task.ProjectId,
            PreviousVersion = latestVersion
        }, cancellationToken);

        if (!agentOutput.Succeeded)
        {
            _logger.LogError("Release Manager agent failed for task {TaskId}: {Error}", taskId, agentOutput.ErrorMessage);
            return Fail($"Release Manager agent failed: {agentOutput.ErrorMessage}");
        }

        // Validate the proposed version is strictly greater than the latest published version.
        if (latestVersion is not null && !SemverHelper.IsGreaterThan(agentOutput.Version!, latestVersion))
        {
            return Fail(
                $"Proposed version '{agentOutput.Version}' is not greater than the latest published version '{latestVersion}'. " +
                "The release version must increment beyond the current latest.");
        }

        // Reject early if the proposed tag already exists on GitHub — don't wait until publish time.
        var proposedTag = $"v{agentOutput.Version}";
        try
        {
            var tagExists = await _releaseService.TagExistsAsync(
                proposedTag, project.RepositoryPath, project.GitHubRepository, cancellationToken);

            if (tagExists)
            {
                _logger.LogError("Tag {Tag} already exists; rejecting proposed version {Version} during preparation", proposedTag, agentOutput.Version);
                return Fail(
                    $"Tag '{proposedTag}' already exists on GitHub. " +
                    "The Release Manager must propose a version higher than any existing tag.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify tag existence for {Tag}; publish-time check will still apply", proposedTag);
        }

        var release = new Core.Domain.Release(
            task.Id,
            agentOutput.Version!,
            agentOutput.Title!,
            agentOutput.Notes!,
            agentOutput.HasBreakingChanges,
            task.MergeCommitSha);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var releaseRepo = scope.ServiceProvider.GetRequiredService<IReleaseRepository>();
            await releaseRepo.AddAsync(release, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist release for task {TaskId}", taskId);
        }

        _logger.LogInformation("Release prepared for task {TaskId}: {Tag}", taskId, release.TagName);
        return ToResult(release, $"Release prepared: {release.TagName} — {release.Title}\nRun /release {taskId.ToString("N")[..8]} approve to publish.");
    }

    public async Task<ReleaseOrchestratorResult> ApproveAndPublishAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        AgentTask? task;
        Core.Domain.Release? release;

        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            task = await taskService.GetTaskAsync(taskId, cancellationToken);
            var releaseRepo = scope.ServiceProvider.GetRequiredService<IReleaseRepository>();
            release = await releaseRepo.GetByTaskIdAsync(taskId, cancellationToken);
        }

        if (task is null)
            return Fail("Task not found.");

        if (release is null)
            return Fail("No release prepared for this task. Run /release first.");

        // Idempotent: already published
        if (release.Status == ReleaseStatus.Published)
        {
            _logger.LogInformation("Release already published for task {TaskId}: {Url}", taskId, release.GitHubReleaseUrl);
            return ToResult(release, $"Release already published: {release.TagName}\n{release.GitHubReleaseUrl}");
        }

        if (release.Status == ReleaseStatus.Failed)
            return Fail("Previous release attempt failed. Run /release to prepare a new release.");

        if (string.IsNullOrWhiteSpace(task.MergeCommitSha))
            return Fail("Task merge commit SHA is missing.");

        var project = _projectRegistry.Find(task.ProjectId);
        if (project is null)
            return Fail($"Project '{task.ProjectId}' is not registered.");

        var validation = await _releaseService.ValidateAsync(cancellationToken);
        if (!validation.IsReady)
        {
            _logger.LogError("GitHub CLI not ready: {Error}", validation.ErrorMessage);
            return Fail($"GitHub CLI not available: {validation.ErrorMessage}");
        }

        var tagExists = await _releaseService.TagExistsAsync(release.TagName, project.RepositoryPath, project.GitHubRepository, cancellationToken);
        if (tagExists)
        {
            _logger.LogError("Tag {Tag} already exists for task {TaskId}", release.TagName, taskId);
            return Fail($"Tag '{release.TagName}' already exists on GitHub. Cannot overwrite an existing release.");
        }

        var createResult = await _releaseService.CreateReleaseAsync(new CreateReleaseRequest
        {
            RepositoryPath = project.RepositoryPath,
            TagName = release.TagName,
            Title = release.Title,
            Notes = release.Notes,
            TargetCommitSha = task.MergeCommitSha,
            GitHubRepository = project.GitHubRepository
        }, cancellationToken);

        if (!createResult.Succeeded)
        {
            _logger.LogError("GitHub release creation failed for task {TaskId}: {Error}", taskId, createResult.ErrorMessage);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var releaseRepo = scope.ServiceProvider.GetRequiredService<IReleaseRepository>();
                release.SetFailed();
                await releaseRepo.UpdateAsync(release, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist release failure for task {TaskId}", taskId);
            }
            return Fail($"GitHub release creation failed: {createResult.ErrorMessage}");
        }

        release.SetPublished(createResult.ReleaseUrl ?? string.Empty);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var releaseRepo = scope.ServiceProvider.GetRequiredService<IReleaseRepository>();
            await releaseRepo.UpdateAsync(release, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist published release for task {TaskId}", taskId);
        }

        _logger.LogInformation("Release {Tag} published for task {TaskId}: {Url}", release.TagName, taskId, release.GitHubReleaseUrl);
        return ToResult(release, $"Release {release.TagName} published.\n{release.GitHubReleaseUrl}");
    }

    private static bool FindingsContain(string? findings, string marker) =>
        findings is not null && findings.Contains(marker, StringComparison.OrdinalIgnoreCase);

    private static ReleaseOrchestratorResult ToResult(Core.Domain.Release release, string summary) => new()
    {
        Succeeded = true,
        Version = release.Version,
        TagName = release.TagName,
        Title = release.Title,
        GitHubReleaseUrl = release.GitHubReleaseUrl,
        Status = release.Status,
        Summary = summary
    };

    private static ReleaseOrchestratorResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error,
        Summary = error
    };
}
