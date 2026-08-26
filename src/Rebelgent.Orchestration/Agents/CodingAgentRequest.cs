namespace Rebelgent.Orchestration.Agents;

/// <summary>Describes what a coding agent should implement in a given worktree.</summary>
public sealed class CodingAgentRequest
{
    public string TaskDescription { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
}
