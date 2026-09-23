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

        await ValidateSourceAsync(project, cancellationToken);
        var remoteRef = $"{project.RemoteName}/{project.DefaultBranch}";

        var shortId = taskId.ToString("N")[..8];
        var branchName = $"rebelgent/task-{shortId}";
        var workspacePath = Path.Combine(root, $"{project.Id}-{shortId}-developer");

        // Safety: workspace must be under the configured root, not under the source repo
        var canonicalRoot = Path.GetFullPath(root);
        var canonicalWorkspace = Path.GetFullPath(workspacePath);
        if (!canonicalWorkspace.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Workspace path '{workspacePath}' is outside configured root '{root}'.");

        // Pre-flight 6: workspace directory must not already exist
        if (Directory.Exists(workspacePath))
            throw new InvalidOperationException($"Workspace directory already exists: '{workspacePath}'. Remove it manually before re-running.");

        _logger.LogInformation("Creating git worktree at {WorkspacePath} from {RemoteRef} for branch {Branch}", workspacePath, remoteRef, branchName);

        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["worktree", "add", workspacePath, "-b", branchName, remoteRef],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 30_000
        }, cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException($"git worktree add failed: {result.StandardError}");

        return new WorkspaceInfo(workspacePath, branchName);
    }

    private async Task ValidateSourceAsync(ProjectDefinition project, CancellationToken cancellationToken)
    {
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

        // Pre-flight 3: source working tree is clean — refuse before any network ops
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

        // Pre-flight 4: fetch from remote so the task branch is always based on the latest remote state,
        // not a potentially stale local copy of the default branch.
        var fetchResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["fetch", project.RemoteName],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 60_000
        }, cancellationToken);
        if (!fetchResult.Success)
            throw new InvalidOperationException($"git fetch {project.RemoteName} failed: {fetchResult.StandardError}");

        // Pre-flight 5: remote default branch must exist after fetch
        var remoteRef = $"{project.RemoteName}/{project.DefaultBranch}";
        var remoteBranchCheckResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["rev-parse", "--verify", remoteRef],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 10_000
        }, cancellationToken);
        if (!remoteBranchCheckResult.Success)
            throw new InvalidOperationException($"Remote branch '{remoteRef}' does not exist in '{project.RepositoryPath}'.");

    }

    public Task<bool> HasDeveloperWorkspaceAsync(ProjectDefinition project, Guid taskId, CancellationToken cancellationToken = default)
    {
        var root = _options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root)) return Task.FromResult(true);
        return Task.FromResult(Directory.Exists(Path.Combine(root, $"{project.Id}-{taskId.ToString("N")[..8]}-developer")));
    }

    public async Task<WorkspaceInfo> CreateForRetryAsync(ProjectDefinition project, Guid taskId, CancellationToken cancellationToken = default)
    {
        var root = _options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("Workspace:RootPath is not configured.");
        if (!Directory.Exists(project.RepositoryPath))
            throw new InvalidOperationException($"Source repository does not exist: {project.RepositoryPath}");

        var shortId = taskId.ToString("N")[..8];
        var branchName = $"rebelgent/task-{shortId}";
        var workspacePath = Path.Combine(root, $"{project.Id}-{shortId}-developer");
        EnsureWorkspacePathIsSafe(root, workspacePath);

        await ValidateSourceAsync(project, cancellationToken);

        var branchExists = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["show-ref", "--verify", "--quiet", $"refs/heads/{branchName}"],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 10_000
        }, cancellationToken);
        if (!branchExists.Success && branchExists.ExitCode != 1)
            throw new InvalidOperationException("Could not inspect the retry branch.");
        if (branchExists.Success)
        {
            var ancestor = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = "git",
                Arguments = ["merge-base", "--is-ancestor", branchName, $"{project.RemoteName}/{project.DefaultBranch}"],
                WorkingDirectory = project.RepositoryPath,
                TimeoutMs = 10_000
            }, cancellationToken);
            if (!ancestor.Success)
                throw new InvalidOperationException("Retry branch contains commits not present on the remote default branch, or its history cannot be verified. Preserve and inspect it before retrying.");
        }

        // The orchestrator has already proved this task has no successful commit, PR, or merge.
        // Only the deterministic task workspace and task branch are touched here.
        if (Directory.Exists(workspacePath))
        {
            var removeResult = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = "git",
                Arguments = ["worktree", "remove", "--force", workspacePath],
                WorkingDirectory = project.RepositoryPath,
                TimeoutMs = 15_000
            }, cancellationToken);
            if (!removeResult.Success)
                throw new InvalidOperationException($"Failed to remove the existing task worktree: {removeResult.StandardError}");

            // Some Git/platform combinations leave the now-unregistered directory behind.
            // The path was derived from the configured root and exact task ID above.
            if (Directory.Exists(workspacePath))
                Directory.Delete(workspacePath, recursive: true);
        }

        var pruneResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["worktree", "prune"],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 15_000
        }, cancellationToken);

        if (!pruneResult.Success)
            throw new InvalidOperationException("Could not prune stale worktree registrations.");

        if (branchExists.Success)
        {
            var deleteResult = await _processRunner.RunAsync(new ProcessRunOptions
            {
                FileName = "git",
                Arguments = ["branch", "-D", branchName],
                WorkingDirectory = project.RepositoryPath,
                TimeoutMs = 15_000
            }, cancellationToken);
            if (!deleteResult.Success)
                throw new InvalidOperationException($"Failed to recreate the failed task branch: {deleteResult.StandardError}");
        }

        return await CreateAsync(project, taskId, cancellationToken);
    }

    public async Task<string> CommitAsync(string workspacePath, string commitMessage, CancellationToken cancellationToken = default)
    {
        // Stage all changes — git add --all respects .gitignore
        var addResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["add", "--all"],
            WorkingDirectory = workspacePath,
            TimeoutMs = 30_000
        }, cancellationToken);

        if (!addResult.Success)
            throw new InvalidOperationException($"git add --all failed: {addResult.StandardError}");

        // Check whether anything was actually staged
        var statusResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["status", "--porcelain"],
            WorkingDirectory = workspacePath,
            TimeoutMs = 10_000
        }, cancellationToken);

        if (!statusResult.Success)
            throw new InvalidOperationException($"git status failed: {statusResult.StandardError}");

        if (string.IsNullOrWhiteSpace(statusResult.StandardOutput))
        {
            // Nothing to commit — return the current HEAD SHA so QA/Reviewer still know which ref to inspect
            _logger.LogWarning("CommitAsync: Nothing to commit in {WorkspacePath}. Returning current HEAD.", workspacePath);
            return await GetHeadShaAsync(workspacePath, cancellationToken);
        }

        // Commit with a fixed Rebelgent identity so no global git config is required
        var commitResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["-c", "user.name=Rebelgent", "-c", "user.email=rebelgent@noreply.local", "commit", "-m", commitMessage],
            WorkingDirectory = workspacePath,
            TimeoutMs = 30_000
        }, cancellationToken);

        if (!commitResult.Success)
            throw new InvalidOperationException($"git commit failed: {commitResult.StandardError}");

        return await GetHeadShaAsync(workspacePath, cancellationToken);
    }

    private async Task<string> GetHeadShaAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var revParseResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["rev-parse", "HEAD"],
            WorkingDirectory = workspacePath,
            TimeoutMs = 10_000
        }, cancellationToken);

        if (!revParseResult.Success)
            throw new InvalidOperationException($"git rev-parse HEAD failed: {revParseResult.StandardError}");

        return revParseResult.StandardOutput.Trim();
    }

    public async Task<WorkspaceInfo> CreateForPackagingAsync(ProjectDefinition project, Guid taskId, string mergeCommitSha, CancellationToken cancellationToken = default)
    {
        var root = _options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("Workspace:RootPath is not configured.");

        if (!Directory.Exists(project.RepositoryPath))
            throw new InvalidOperationException($"Source repository does not exist: {project.RepositoryPath}");

        if (string.IsNullOrWhiteSpace(mergeCommitSha))
            throw new ArgumentException("mergeCommitSha must not be empty.", nameof(mergeCommitSha));

        // Fetch so the merged commit is reachable even when local default branch is stale
        _logger.LogInformation("Fetching from {Remote} before creating package worktree", project.RemoteName);
        var fetchResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["fetch", project.RemoteName],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 60_000
        }, cancellationToken);
        if (!fetchResult.Success)
            throw new InvalidOperationException($"git fetch {project.RemoteName} failed: {fetchResult.StandardError}");

        // Verify the commit is reachable after fetch
        var verifyResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["rev-parse", "--verify", mergeCommitSha],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 10_000
        }, cancellationToken);
        if (!verifyResult.Success)
            throw new InvalidOperationException(
                $"Merge commit '{mergeCommitSha}' is not reachable in '{project.RepositoryPath}' after fetch. " +
                "Ensure the commit exists on the remote.");

        var shortId = taskId.ToString("N")[..8];
        var workspacePath = Path.Combine(root, $"{project.Id}-{shortId}-package");

        var canonicalRoot = Path.GetFullPath(root);
        var canonicalWorkspace = Path.GetFullPath(workspacePath);
        if (!canonicalWorkspace.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Package workspace '{workspacePath}' is outside configured root '{root}'.");

        if (Directory.Exists(workspacePath))
            throw new InvalidOperationException(
                $"Package workspace already exists: '{workspacePath}'. " +
                "Remove it manually (git worktree remove --force) before retrying.");

        _logger.LogInformation("Creating package worktree at {WorkspacePath} from commit {Sha}", workspacePath, mergeCommitSha);

        var addResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["worktree", "add", "--detach", workspacePath, mergeCommitSha],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 30_000
        }, cancellationToken);

        if (!addResult.Success)
            throw new InvalidOperationException($"git worktree add (package) failed: {addResult.StandardError}");

        return new WorkspaceInfo(workspacePath, mergeCommitSha);
    }

    public async Task<WorkspaceInfo> CreateFromBranchAsync(ProjectDefinition project, string existingBranch, string roleSuffix, string? commitSha = null, CancellationToken cancellationToken = default)
    {
        var root = _options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("Workspace:RootPath is not configured.");

        if (!Directory.Exists(project.RepositoryPath))
            throw new InvalidOperationException($"Source repository does not exist: {project.RepositoryPath}");

        if (string.IsNullOrWhiteSpace(roleSuffix) || !roleSuffix.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
            throw new ArgumentException("roleSuffix must be alphanumeric with dash or underscore only.", nameof(roleSuffix));

        var branchSlug = existingBranch.Replace('/', '-').Replace('\\', '-');
        if (branchSlug.Length > 30) branchSlug = branchSlug[..30];

        var workspacePath = Path.Combine(root, $"{project.Id}-{branchSlug}-{roleSuffix}");

        var canonicalRoot = Path.GetFullPath(root);
        var canonicalWorkspace = Path.GetFullPath(workspacePath);
        if (!canonicalWorkspace.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Workspace path '{workspacePath}' is outside configured root '{root}'.");

        if (Directory.Exists(workspacePath))
            throw new InvalidOperationException($"Workspace directory already exists: '{workspacePath}'. Remove it manually before re-running.");

        // Prefer exact commit SHA so QA/Reviewer see the developer's committed state, not just the branch tip
        var gitRef = commitSha ?? existingBranch;
        _logger.LogInformation("Creating QA/Reviewer worktree at {WorkspacePath} from ref {Ref}", workspacePath, gitRef);

        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["worktree", "add", "--detach", workspacePath, gitRef],
            WorkingDirectory = project.RepositoryPath,
            TimeoutMs = 30_000
        }, cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException($"git worktree add (detached) failed: {result.StandardError}");

        return new WorkspaceInfo(workspacePath, existingBranch);
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

    private static void EnsureWorkspacePathIsSafe(string root, string workspacePath)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var canonicalWorkspace = Path.GetFullPath(workspacePath);
        if (!canonicalWorkspace.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Workspace path '{workspacePath}' is outside configured root '{root}'.");
    }
}
