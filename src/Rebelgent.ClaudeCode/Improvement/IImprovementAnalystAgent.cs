namespace Rebelgent.ClaudeCode.Improvement;

/// <summary>
/// Analyzes a single detected failure pattern and drafts a structured improvement proposal.
/// Read-only: never touches a git worktree, never modifies source, never commits or pushes.
/// The agent may only produce a proposal — applying it always requires a human-approved,
/// normal Rebelgent development task.
/// </summary>
public interface IImprovementAnalystAgent
{
    Task<ImprovementAnalysisOutput> AnalyzeAsync(ImprovementAnalysisInput input, CancellationToken cancellationToken = default);
}
