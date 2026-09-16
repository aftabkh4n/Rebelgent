using System.Text.RegularExpressions;
using Rebelgent.Core.Domain;

namespace Rebelgent.ClaudeCode.Improvement;

/// <summary>
/// Builds a local evaluation dataset from the historical failures a proposal was raised from and
/// scores the proposal against it. Purely data-driven — it never executes code, never touches a
/// workspace, and never modifies production behavior. Requires no external or paid service, so it
/// runs at $0 and works fully offline.
///
/// The check is deliberately simple and deterministic: does the proposal's own target area /
/// suggested change / description textually engage with the failure category or source it claims
/// to address? This catches proposals that drifted from their evidence (e.g. a generic non-answer)
/// without ever running the proposed change against real code.
/// </summary>
internal static class RegressionEvaluator
{
    public static EvaluationDataset BuildAndScore(
        string datasetName,
        ImprovementProposal proposal,
        IReadOnlyList<ExecutionFailure> sampleFailures)
    {
        var proposalText = $"{proposal.TargetArea} {proposal.SuggestedChange} {proposal.Description}".ToLowerInvariant();

        var cases = sampleFailures.Select((failure, index) =>
        {
            var categoryWords = SplitWords(failure.Category.ToString());
            var addressesCategory = categoryWords.Any(w => w.Length > 3 && proposalText.Contains(w));
            var addressesSource = proposalText.Contains(failure.Source.ToLowerInvariant());
            var passed = addressesCategory || addressesSource;

            var expected = $"Suggested change references the {failure.Category} failure category or the {failure.Source} source.";
            var actual = passed
                ? "Suggested change / target area textually references the failure category or source."
                : "Suggested change / target area does not clearly reference the failure category or source.";

            return new EvaluationCase(
                Name: $"case-{index + 1}: {failure.Category} in {failure.Source} ({failure.DetectedAt:yyyy-MM-dd})",
                ExpectedOutcome: expected,
                ActualOutcome: actual,
                Passed: passed,
                Notes: "Deterministic structural relevance check — evaluates textual alignment only; never executes code or touches a workspace.");
        }).ToList();

        return new EvaluationDataset(datasetName, cases);
    }

    private static IReadOnlyList<string> SplitWords(string pascalCaseCategory) =>
        Regex.Replace(pascalCaseCategory, "(?<!^)([A-Z])", " $1")
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
