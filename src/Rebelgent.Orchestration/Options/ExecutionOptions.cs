namespace Rebelgent.Orchestration.Options;

/// <summary>Timeout configuration for agent execution phases.</summary>
public sealed class ExecutionOptions
{
    public const string SectionName = "Execution";

    public int ClaudeTimeoutMinutes { get; set; } = 15;
    public int BuildTimeoutMinutes { get; set; } = 5;
    public int TestTimeoutMinutes { get; set; } = 5;
}
