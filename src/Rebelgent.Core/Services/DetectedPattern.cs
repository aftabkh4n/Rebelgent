using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Services;

/// <summary>A recurring failure pattern deterministically detected across historical task
/// executions — the structured evidence an improvement proposal is drafted from.</summary>
public sealed record DetectedPattern(
    string Title,
    string Evidence,
    FailureCategory Category,
    string Source,
    string TargetArea,
    int Occurrences,
    string EvidenceFingerprint);
