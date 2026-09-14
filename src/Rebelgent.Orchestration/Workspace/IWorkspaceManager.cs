using Rebelgent.Orchestration.Projects;

namespace Rebelgent.Orchestration.Workspace;

/// <summary>Creates and removes isolated git worktrees for agent execution.</summary>
public interface IWorkspaceManager
{
    Task<WorkspaceInfo> CreateAsync(ProjectDefinition project, Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Creates a detached worktree from a branch or exact commit SHA (for QA/Reviewer — does not create a new branch).</summary>
    Task<WorkspaceInfo> CreateFromBranchAsync(ProjectDefinition project, string existingBranch, string roleSuffix, string? commitSha = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches from remote, verifies the merge commit SHA is reachable, then creates a detached worktree at
    /// that exact commit. Used for NuGet packaging to guarantee the merged code is present regardless of
    /// whether the local default branch has been updated. Never modifies the source repository.
    /// </summary>
    Task<WorkspaceInfo> CreateForPackagingAsync(ProjectDefinition project, Guid taskId, string mergeCommitSha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages all changes in the worktree (respecting .gitignore) and commits them.
    /// Returns the resulting commit SHA. If there is nothing to commit, returns the current HEAD SHA without creating a commit.
    /// </summary>
    Task<string> CommitAsync(string workspacePath, string commitMessage, CancellationToken cancellationToken = default);

    Task RemoveAsync(string workspacePath, CancellationToken cancellationToken = default);
}
