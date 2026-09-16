namespace Rebelgent.ClaudeCode.Improvement;

public sealed class ImprovementAnalysisInput
{
    public required string Title { get; init; }
    public required string Category { get; init; }
    public required string Source { get; init; }
    public required int Occurrences { get; init; }
    public required string Evidence { get; init; }
}
