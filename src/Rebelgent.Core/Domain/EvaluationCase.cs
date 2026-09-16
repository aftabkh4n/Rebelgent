namespace Rebelgent.Core.Domain;

/// <summary>A single named evaluation case: an expected outcome compared against an actual
/// outcome, deterministically scored pass/fail. Provider-independent, requires no external service.</summary>
public sealed record EvaluationCase(
    string Name,
    string ExpectedOutcome,
    string ActualOutcome,
    bool Passed,
    string? Notes);
