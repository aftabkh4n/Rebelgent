namespace Rebelgent.Core.Services;

/// <summary>Configuration for the agent evolution pipeline. Safety gates are hardcoded and not configurable.</summary>
public sealed class AgentEvolutionOptions
{
    public const string SectionName = "AgentEvolution";

    public bool Enabled { get; init; } = true;
    public int AnalyzeEveryCompletedTasks { get; init; } = 10;
    public int MinimumEvidenceCount { get; init; } = 3;
    public bool AutoCreateProposal { get; init; } = true;

    /// <summary>Registered project ID under which approved evolution proposals are turned into
    /// normal implementation tasks. Validated against <c>IProjectRegistry</c> at analyze-time and
    /// again at approve-time. If missing or unregistered the pipeline fails closed — no proposal
    /// is created and no task is created.</summary>
    public string ProjectId { get; init; } = string.Empty;

    // HARDCODED SAFETY: these MUST NOT be made configurable
    public bool AutoActivateAgent => false;
    public bool AutoRetireAgent => false;
    public bool AutoSuspendAgent => false;
    public bool AutoApproveEvolution => false;
}
