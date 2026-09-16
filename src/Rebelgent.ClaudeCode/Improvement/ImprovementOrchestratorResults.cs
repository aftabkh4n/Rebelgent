using Rebelgent.Core.Domain;

namespace Rebelgent.ClaudeCode.Improvement;

public sealed class ImprovementAnalyzeResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string Summary { get; init; } = string.Empty;
    public int PatternsDetected { get; init; }
    public int ProposalsCreated { get; init; }
    public int DuplicatesSkipped { get; init; }
    public int AnalystFailures { get; init; }
    public int AnalystNoProposals { get; init; }
    public int ParseFailures { get; init; }
    public int EvaluationFailures { get; init; }
    public int PersistenceFailures { get; init; }
    public IReadOnlyList<ImprovementProposal> NewProposals { get; init; } = [];
}

public sealed class ImprovementDecisionResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ImprovementProposal? Proposal { get; init; }
}
