namespace Rebelgent.ClaudeCode.Evolution;

/// <summary>Input data for the agent evolution manager analysis.</summary>
public sealed class AgentEvolutionAnalysisInput
{
    public required string PatternSummary { get; init; }
    public required string AgentPerformanceSummary { get; init; }
    public required string FailureCategorySummary { get; init; }
    public required string ExistingAgentsSummary { get; init; }
    public string Evidence { get; init; } = string.Empty;
}
