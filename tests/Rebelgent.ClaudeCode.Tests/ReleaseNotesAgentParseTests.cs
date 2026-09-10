using Rebelgent.ClaudeCode.ReleaseNotes;

namespace Rebelgent.ClaudeCode.Tests;

public class ReleaseNotesAgentParseTests
{
    private static string ValidOutput(string version = "1.2.3", string title = "Feature release",
        string breaking = "false", string notes = "## What's Changed\n- Added feature") =>
        $"RELEASE_VERSION: {version}\nRELEASE_TITLE: {title}\nBREAKING_CHANGES: {breaking}\nRELEASE_NOTES:\n{notes}";

    [Fact]
    public void ParseOutput_WellFormedOutput_ReturnsSuccess()
    {
        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(ValidOutput());

        Assert.True(result.Succeeded);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("Feature release", result.Title);
        Assert.False(result.HasBreakingChanges);
        Assert.Contains("What's Changed", result.Notes);
    }

    [Fact]
    public void ParseOutput_EmptyString_ReturnsFail()
    {
        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(string.Empty);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ParseOutput_WhitespaceOnly_ReturnsFail()
    {
        var result = ClaudeCodeReleaseNotesAgent.ParseOutput("   \n   ");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ParseOutput_ValidSemver_Accepted()
    {
        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(ValidOutput(version: "2.10.3"));

        Assert.True(result.Succeeded);
        Assert.Equal("2.10.3", result.Version);
    }

    [Fact]
    public void ParseOutput_InvalidVersionNonSemver_ReturnsFail()
    {
        var raw = "RELEASE_VERSION: not-a-version\nRELEASE_TITLE: Title\nRELEASE_NOTES:\nnotes";

        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(raw);

        Assert.False(result.Succeeded);
        Assert.Contains("invalid version", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseOutput_NoVersionLineButVersionInText_FallsBackToExtract()
    {
        var raw = "Here is the release for version 3.0.1\nRELEASE_TITLE: My Release\nRELEASE_NOTES:\n- stuff";

        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(raw);

        Assert.True(result.Succeeded);
        Assert.Equal("3.0.1", result.Version);
    }

    [Fact]
    public void ParseOutput_NoVersionAnywhere_ReturnsFail()
    {
        var raw = "RELEASE_TITLE: My Release\nRELEASE_NOTES:\n- stuff";

        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(raw);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ParseOutput_BreakingChangesTrue_SetsFlag()
    {
        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(ValidOutput(breaking: "true"));

        Assert.True(result.Succeeded);
        Assert.True(result.HasBreakingChanges);
    }

    [Fact]
    public void ParseOutput_BreakingChangesFalse_ClearsFlag()
    {
        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(ValidOutput(breaking: "false"));

        Assert.True(result.Succeeded);
        Assert.False(result.HasBreakingChanges);
    }

    [Fact]
    public void ParseOutput_MissingTitle_DefaultsTitleToReleaseVersion()
    {
        var raw = "RELEASE_VERSION: 1.0.0\nRELEASE_NOTES:\n- something";

        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(raw);

        Assert.True(result.Succeeded);
        Assert.Equal("Release 1.0.0", result.Title);
    }

    [Fact]
    public void ParseOutput_MissingNotesSection_FallsBackToRawOutput()
    {
        var raw = "RELEASE_VERSION: 1.0.0\nRELEASE_TITLE: My Release";

        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(raw);

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Notes));
    }

    [Fact]
    public void ParseOutput_VersionWithLeadingV_NotAcceptedDirectly()
    {
        // Claude may produce "v1.0.0" — semver validator expects digits only
        // The system should either extract "1.0.0" from it or fail gracefully
        var raw = "RELEASE_VERSION: v1.0.0\nRELEASE_TITLE: Title\nRELEASE_NOTES:\nnotes";

        // Either succeeds (extracted 1.0.0) or fails with clear message — both are acceptable
        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(raw);

        if (result.Succeeded)
            Assert.Matches(@"^\d+\.\d+\.\d+$", result.Version);
        else
            Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ParseOutput_RawOutputIsPreserved()
    {
        var raw = ValidOutput();

        var result = ClaudeCodeReleaseNotesAgent.ParseOutput(raw);

        Assert.Equal(raw, result.RawOutput);
    }
}
