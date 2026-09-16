using Rebelgent.ClaudeCode.Improvement;
using Rebelgent.Core.Domain;

namespace Rebelgent.ClaudeCode.Tests;

public class RegressionEvaluatorTests
{
    private static ImprovementProposal MakeProposal(string targetArea, string suggestedChange, string description = "reasoning") =>
        new(
            targetProjectId: "sandbox",
            title: "Some pattern",
            description: description,
            evidence: "evidence text",
            targetArea: targetArea,
            suggestedChange: suggestedChange,
            riskLevel: RiskLevel.Low,
            evidenceFingerprint: "fingerprint");

    private static ExecutionFailure MakeFailure(FailureCategory category, string source) =>
        new(Guid.NewGuid(), null, category, source, "some failure message");

    [Fact]
    public void BuildAndScore_SuggestedChangeReferencesCategory_CasePasses()
    {
        var proposal = MakeProposal("Developer Prompt", "Add a reminder about build failures before commit.");
        var failures = new[] { MakeFailure(FailureCategory.BuildFailure, "BackendDeveloper") };

        var dataset = RegressionEvaluator.BuildAndScore("dataset", proposal, failures);

        Assert.True(dataset.Cases.Single().Passed);
    }

    [Fact]
    public void BuildAndScore_SuggestedChangeReferencesSource_CasePasses()
    {
        var proposal = MakeProposal("Prompt Updates", "Update the BackendDeveloper prompt to add a checklist step.");
        var failures = new[] { MakeFailure(FailureCategory.BuildFailure, "BackendDeveloper") };

        var dataset = RegressionEvaluator.BuildAndScore("dataset", proposal, failures);

        Assert.True(dataset.Cases.Single().Passed);
    }

    [Fact]
    public void BuildAndScore_GenericUnrelatedChange_CaseFails()
    {
        var proposal = MakeProposal("General Improvements", "Consider improving things in general.");
        var failures = new[] { MakeFailure(FailureCategory.BuildFailure, "BackendDeveloper") };

        var dataset = RegressionEvaluator.BuildAndScore("dataset", proposal, failures);

        Assert.False(dataset.Cases.Single().Passed);
    }

    [Fact]
    public void BuildAndScore_MultipleFailures_ProducesOneCasePerFailure()
    {
        var proposal = MakeProposal("Developer Prompt", "Add a build reminder.");
        var failures = new[]
        {
            MakeFailure(FailureCategory.BuildFailure, "BackendDeveloper"),
            MakeFailure(FailureCategory.BuildFailure, "BackendDeveloper"),
            MakeFailure(FailureCategory.BuildFailure, "BackendDeveloper")
        };

        var dataset = RegressionEvaluator.BuildAndScore("dataset", proposal, failures);

        Assert.Equal(3, dataset.Cases.Count);
    }

    [Fact]
    public void BuildAndScore_NeverThrowsForEmptyFailureList()
    {
        var proposal = MakeProposal("Developer Prompt", "Add a build reminder.");

        var dataset = RegressionEvaluator.BuildAndScore("dataset", proposal, []);

        Assert.Empty(dataset.Cases);
    }

    [Fact]
    public void BuildAndScore_CaseNotes_NeverClaimToExecuteCode()
    {
        var proposal = MakeProposal("Developer Prompt", "Add a build reminder.");
        var failures = new[] { MakeFailure(FailureCategory.BuildFailure, "BackendDeveloper") };

        var dataset = RegressionEvaluator.BuildAndScore("dataset", proposal, failures);

        Assert.Contains("never executes code", dataset.Cases.Single().Notes, StringComparison.OrdinalIgnoreCase);
    }
}
