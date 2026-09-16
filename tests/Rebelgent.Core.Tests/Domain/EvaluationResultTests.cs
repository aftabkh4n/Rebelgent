using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Tests.Domain;

public class EvaluationResultTests
{
    private static readonly EvaluationCase PassCase = new("case-1", "expected", "actual: matches", true, "notes");
    private static readonly EvaluationCase FailCase = new("case-2", "expected", "actual: does not match", false, "notes");

    [Fact]
    public void Constructor_ComputesTotalAndPassedCases()
    {
        var result = new EvaluationResult(Guid.NewGuid(), "dataset-1", [PassCase, FailCase], "notes");

        Assert.Equal(2, result.TotalCases);
        Assert.Equal(1, result.PassedCases);
    }

    [Fact]
    public void Constructor_ComputesPassRate()
    {
        var result = new EvaluationResult(Guid.NewGuid(), "dataset-1", [PassCase, PassCase, FailCase], "notes");

        Assert.Equal(2d / 3d, result.PassRate, precision: 6);
    }

    [Fact]
    public void Constructor_EmptyCases_PassRateIsZero()
    {
        var result = new EvaluationResult(Guid.NewGuid(), "dataset-1", [], "no cases");

        Assert.Equal(0, result.TotalCases);
        Assert.Equal(0, result.PassRate);
    }

    [Fact]
    public void Constructor_EmptyProposalId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new EvaluationResult(Guid.Empty, "dataset-1", [PassCase], "notes"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyDatasetName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new EvaluationResult(Guid.NewGuid(), name, [PassCase], "notes"));
    }

    [Fact]
    public void Cases_RoundTripsThroughSerialization()
    {
        var result = new EvaluationResult(Guid.NewGuid(), "dataset-1", [PassCase, FailCase], "notes");

        var cases = result.Cases;

        Assert.Equal(2, cases.Count);
        Assert.Equal(PassCase, cases[0]);
        Assert.Equal(FailCase, cases[1]);
    }
}
