using Rebelgent.ClaudeCode.Improvement;

namespace Rebelgent.ClaudeCode.Tests;

public class ImprovementAnalystAgentParseTests
{
    private static string ValidOutput(
        string title = "Add build reminder to Developer prompt",
        string targetArea = "Developer Prompt",
        string riskLevel = "Low",
        string suggestedChange = "Add an explicit build-before-commit reminder.",
        string description = "The Developer agent has repeatedly failed the build for the same reason.") =>
        $"PROPOSAL_TITLE: {title}\nTARGET_AREA: {targetArea}\nRISK_LEVEL: {riskLevel}\nSUGGESTED_CHANGE: {suggestedChange}\nDESCRIPTION:\n{description}";

    [Fact]
    public void ParseOutput_WellFormedOutput_ReturnsSuccess()
    {
        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput(ValidOutput());

        Assert.True(result.Succeeded);
        Assert.Equal("Add build reminder to Developer prompt", result.ProposalTitle);
        Assert.Equal("Developer Prompt", result.TargetArea);
        Assert.Equal("Low", result.RiskLevel);
        Assert.Equal("Add an explicit build-before-commit reminder.", result.SuggestedChange);
        Assert.Contains("repeatedly failed the build", result.Description);
    }

    [Fact]
    public void ParseOutput_WellFormedOutput_Returns_NoneKind()
    {
        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput(ValidOutput());

        Assert.Equal(AnalystOutputKind.None, result.FailureKind);
    }

    [Fact]
    public void ParseOutput_EmptyString_ReturnsFail()
    {
        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput(string.Empty);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ParseOutput_EmptyString_Returns_EmptyOutputKind()
    {
        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput(string.Empty);

        Assert.Equal(AnalystOutputKind.EmptyOutput, result.FailureKind);
    }

    [Fact]
    public void ParseOutput_WhitespaceOnly_ReturnsFail()
    {
        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput("   \n   ");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ParseOutput_WhitespaceOnly_Returns_EmptyOutputKind()
    {
        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput("   \n   ");

        Assert.Equal(AnalystOutputKind.EmptyOutput, result.FailureKind);
    }

    [Fact]
    public void ParseOutput_MissingTitle_ReturnsFail()
    {
        var raw = "TARGET_AREA: QA Checklist\nRISK_LEVEL: Low\nSUGGESTED_CHANGE: change\nDESCRIPTION:\ndesc";

        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput(raw);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ParseOutput_MissingTitle_Returns_ParseFailedKind()
    {
        var raw = "TARGET_AREA: QA Checklist\nRISK_LEVEL: Low\nSUGGESTED_CHANGE: change\nDESCRIPTION:\ndesc";

        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput(raw);

        Assert.Equal(AnalystOutputKind.ParseFailed, result.FailureKind);
    }

    [Fact]
    public void ParseOutput_MissingDescription_FallsBackToRawOutput()
    {
        var raw = "PROPOSAL_TITLE: Title\nTARGET_AREA: Area\nRISK_LEVEL: Low\nSUGGESTED_CHANGE: change";

        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput(raw);

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
    }

    [Fact]
    public void ParseOutput_RawOutputIsPreserved()
    {
        var raw = ValidOutput();

        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput(raw);

        Assert.Equal(raw, result.RawOutput);
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    [InlineData("Critical")]
    public void ParseOutput_AllRiskLevels_ParsedVerbatim(string riskLevel)
    {
        var result = ClaudeCodeImprovementAnalystAgent.ParseOutput(ValidOutput(riskLevel: riskLevel));

        Assert.Equal(riskLevel, result.RiskLevel);
    }
}
