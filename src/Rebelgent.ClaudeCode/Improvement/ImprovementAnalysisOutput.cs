namespace Rebelgent.ClaudeCode.Improvement;

public enum AnalystOutputKind
{
    None = 0,
    ProcessFailed = 1,    // non-zero exit, timeout, config error, dir creation error
    EmptyOutput = 2,      // exit 0, but empty/whitespace output (AnalystReturnedNoProposal)
    ParseFailed = 3       // exit 0, non-empty output, PROPOSAL_TITLE missing
}

public sealed class ImprovementAnalysisOutput
{
    public bool Succeeded { get; init; }
    public string? ProposalTitle { get; init; }
    public string? TargetArea { get; init; }
    public string? SuggestedChange { get; init; }
    public string? RiskLevel { get; init; }
    public string? Description { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RawOutput { get; init; }
    public AnalystOutputKind FailureKind { get; init; }
}
