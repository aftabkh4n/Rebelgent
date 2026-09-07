namespace Rebelgent.Orchestration.Projects;

/// <summary>A registered project that agents are permitted to work on.</summary>
public sealed class ProjectDefinition
{
    /// <summary>Short identifier used in Telegram commands (e.g. "sandbox").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path to the source repository root on this host.</summary>
    public string RepositoryPath { get; set; } = string.Empty;

    /// <summary>The default branch to create agent worktrees from (e.g. "main").</summary>
    public string DefaultBranch { get; set; } = "main";

    /// <summary>Git remote name to push agent branches to (e.g. "origin").</summary>
    public string RemoteName { get; set; } = "origin";

    /// <summary>GitHub repository in owner/repo format (e.g. "myorg/myrepo"). Optional — required for PR creation.</summary>
    public string? GitHubRepository { get; set; }
}
