namespace Rebelgent.Orchestration.Agents;

/// <summary>Provider-independent interface for running a coding agent against a worktree.</summary>
public interface ICodingAgentRunner
{
    /// <summary>Verifies the agent is configured and reachable before a worktree is created.</summary>
    Task<AgentValidationResult> ValidateAsync(CancellationToken cancellationToken = default);

    Task<CodingAgentResult> RunAsync(CodingAgentRequest request, CancellationToken cancellationToken = default);
}
