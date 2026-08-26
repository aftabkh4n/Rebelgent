using Rebelgent.Orchestration.Projects;

namespace Rebelgent.Orchestration.Workspace;

/// <summary>Creates and removes isolated git worktrees for agent execution.</summary>
public interface IWorkspaceManager
{
    Task<WorkspaceInfo> CreateAsync(ProjectDefinition project, Guid taskId, CancellationToken cancellationToken = default);

    Task RemoveAsync(string workspacePath, CancellationToken cancellationToken = default);
}
