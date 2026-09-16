namespace Rebelgent.ClaudeCode.Improvement;

/// <summary>
/// Configures which registered project <c>/improve analyze</c> analyzes for self-improvement.
/// Must be a project ID already registered in <c>Projects:Projects</c> (validated via
/// <c>IProjectRegistry</c>) — never a raw filesystem path.
/// </summary>
public sealed class SelfImprovementOptions
{
    public const string SectionName = "SelfImprovement";

    /// <summary>Registered project ID to analyze, e.g. "rebelgent". Must be explicitly configured —
    /// there is no default and no fallback to a hardcoded path.</summary>
    public string? ProjectId { get; set; }
}
