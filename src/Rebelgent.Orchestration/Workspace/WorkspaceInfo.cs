namespace Rebelgent.Orchestration.Workspace;

/// <summary>Describes an isolated git worktree created for one agent execution.</summary>
public sealed class WorkspaceInfo
{
    public string WorkspacePath { get; }
    public string BranchName { get; }

    public WorkspaceInfo(string workspacePath, string branchName)
    {
        WorkspacePath = workspacePath;
        BranchName = branchName;
    }
}
