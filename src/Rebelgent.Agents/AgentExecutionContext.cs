namespace Rebelgent.Agents;

/// <summary>Carries runtime context that an agent needs during execution.</summary>
public class AgentExecutionContext
{
    /// <summary>Arbitrary key-value metadata the orchestrator may pass to the agent.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public AgentExecutionContext(IReadOnlyDictionary<string, string>? metadata = null)
    {
        Metadata = metadata ?? new Dictionary<string, string>();
    }
}
