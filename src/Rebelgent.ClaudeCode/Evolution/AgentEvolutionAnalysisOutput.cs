namespace Rebelgent.ClaudeCode.Evolution;

/// <summary>Output from the agent evolution manager agent.</summary>
public sealed class AgentEvolutionAnalysisOutput
{
    public bool Succeeded { get; init; }
    public string? ProposalTitle { get; init; }
    public string? ProposedAgentName { get; init; }

    /// <summary>"CreateAgent", "CreateNewVersion", etc.</summary>
    public string? ProposalType { get; init; }
    public string? RiskLevel { get; init; }
    public string? SuggestedChange { get; init; }
    public string? SuggestedPrompt { get; init; }
    public string? Description { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RawOutput { get; init; }
    public EvolutionManagerOutputKind FailureKind { get; init; }
}

public enum EvolutionManagerOutputKind
{
    None = 0,
    ProcessFailed = 1,
    EmptyOutput = 2,
    ParseFailed = 3
}
