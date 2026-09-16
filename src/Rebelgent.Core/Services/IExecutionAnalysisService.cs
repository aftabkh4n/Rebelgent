namespace Rebelgent.Core.Services;

/// <summary>
/// Deterministically analyzes historical task/execution/release/package data to categorize
/// failures and detect recurring patterns. Contains no LLM calls and no provider-specific code —
/// pattern detection is pure, rule-based analysis over already-persisted Core data.
/// Always scoped to a single project ID — analysis must know which project it is analyzing.
/// </summary>
public interface IExecutionAnalysisService
{
    /// <summary>Scans recent agent executions, releases, and packages belonging to
    /// <paramref name="projectId"/> for failures not yet categorized, categorizes them
    /// deterministically, and persists them. Safe to call repeatedly — already-categorized
    /// failures are never duplicated.</summary>
    Task<int> CategorizeAndPersistFailuresAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>Re-categorizes recent history for <paramref name="projectId"/> (see
    /// <see cref="CategorizeAndPersistFailuresAsync"/>) then groups the resulting failures to
    /// detect recurring patterns with at least <paramref name="minOccurrences"/> occurrences.</summary>
    Task<IReadOnlyList<DetectedPattern>> AnalyzeAsync(string projectId, int minOccurrences = 2, CancellationToken cancellationToken = default);
}
