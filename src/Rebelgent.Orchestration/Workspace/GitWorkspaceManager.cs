using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Process;
using Rebelgent.Orchestration.Projects;

namespace Rebelgent.Orchestration.Workspace;

/// <summary>
/// Creates isolated git worktrees under the configured workspace root.
/// The worktree is always placed under WorkspaceOptions.RootPath — never under the source repo.
/// </summary>
internal sealed class GitWorkspaceManager : IWorkspaceManager
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<WorkspaceOptions> _options;
    private readonly ILogger<GitWorkspaceManager> _logger;

    public GitWorkspaceManager(
        IProcessRunner processRunner,
        IOptions<WorkspaceOptions> options,
        ILogger<GitWorkspaceManager> logger)
    {
        _processRunner = processRunner;
        _options = options;
        _logger = logger;
    }

    public async Task<WorkspaceInfo> CreateAsync(ProjectDefinition project, Guid taskId, CancellationToken cancellationToken = default)
    {
        var root = _options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("Workspace:RootPath is not configured.");

        // Pre-flight 1: source repository directory exists
        if (!Directory.Exists(project.RepositoryPath))
            throw new InvalidOperationException($"Source repository does not exist: {project.RepositoryPath}");

        // Pre-flight 2: source path is a git repository
        var gitCheckResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["rev-parse", "--git-dir"],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 10_000
        }, cancellationToken);
        if (!gitCheckResult.Success)
            throw new InvalidOperationException($"'{project.RepositoryPath}' is not a git repository.");

        // Pre-flight 3: default branch exists
        var branchCheckResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["rev-parse", "--verify", project.DefaultBranch],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 10_000
        }, cancellationToken);
        if (!branchCheckResult.Success)
            throw new InvalidOperationException($"Branch '{project.DefaultBranch}' does not exist in '{project.RepositoryPath}'.");

        // Pre-flight 4: source working tree is clean — refuse if dirty
        var statusResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["status", "--porcelain"],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 10_000
        }, cancellationToken);
        if (!statusResult.Success)
            throw new InvalidOperationException($"Could not check git status for '{project.RepositoryPath}'.");
        if (!string.IsNullOrWhiteSpace(statusResult.StandardOutput))
            throw new InvalidOperationException(
                $"Source repository '{project.RepositoryPath}' has uncommitted changes. Stash or commit changes before running an agent.");

        var shortId = taskId.ToString("N")[..8];
        var branchName = $"rebelgent/task-{shortId}";
        var workspacePath = Path.Combine(root, $"{project.Id}-{shortId}-developer");

        // Safety: workspace must be under the configured root, not under the source repo
        var canonicalRoot = Path.GetFullPath(root);
        var canonicalWorkspace = Path.GetFullPath(workspacePath);
        if (!canonicalWorkspace.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Workspace path '{workspacePath}' is outside configured root '{root}'.");

        // Pre-flight 5: workspace directory must not already exist
        if (Directory.Exists(workspacePath))
            throw new InvalidOperationException($"Workspace directory already exists: '{workspacePath}'. Remove it manually before re-running.");

        _logger.LogInformation("Creating git worktree at {WorkspacePath} for branch {Branch}", workspacePath, branchName);

        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["worktree", "add", workspacePath, "-b", branchName, project.DefaultBranch],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 30_000
        }, cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException($"git worktree add failed: {result.StandardError}");

        return new WorkspaceInfo(workspacePath, branchName);
    }

    public async Task RemoveAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        // Safety: we pass the specific path only, not a shell command built from user input
        _logger.LogInformation("Removing git worktree at {WorkspacePath}", workspacePath);

        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["worktree", "remove", "--force", workspacePath],
            WorkingDirectory = workspacePath,
            TimeoutMs = 15_000
        }, cancellationToken);

        if (!result.Success)
            _logger.LogWarning("git worktree remove returned non-zero: {Error}", result.StandardError);
    }
}
