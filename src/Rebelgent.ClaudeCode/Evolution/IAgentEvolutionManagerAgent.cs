namespace Rebelgent.ClaudeCode.Evolution;

/// <summary>Agent that analyzes system patterns and proposes agent evolution actions.</summary>
public interface IAgentEvolutionManagerAgent
{
    Task<AgentEvolutionAnalysisOutput> AnalyzeAsync(AgentEvolutionAnalysisInput input, CancellationToken ct = default);
}
