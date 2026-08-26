namespace Rebelgent.Orchestration.Options;

/// <summary>Configuration for agent workspace root directory.</summary>
public sealed class WorkspaceOptions
{
    public const string SectionName = "Workspace";

    /// <summary>Absolute path to the root directory where agent worktrees are created.</summary>
    public string RootPath { get; set; } = string.Empty;
}
