using Rebelgent.ClaudeCode;

namespace Rebelgent.ClaudeCode.Tests;

public class ReviewerPromptBuilderTests
{
    [Fact]
    public void Build_ContainsSystemInstructions()
    {
        var prompt = ReviewerPromptBuilder.Build("task", @"C:\ws", "proj", null);
        Assert.Contains("[SYSTEM INSTRUCTIONS", prompt);
    }

    [Fact]
    public void Build_ContainsTaskDataDelimiters()
    {
        var prompt = ReviewerPromptBuilder.Build("task", @"C:\ws", "proj", null);
        Assert.Contains("--- BEGIN TASK DATA ---", prompt);
        Assert.Contains("--- END TASK DATA ---", prompt);
    }

    [Fact]
    public void Build_ContainsQaFindingsDelimiters()
    {
        var prompt = ReviewerPromptBuilder.Build("task", @"C:\ws", "proj", "QA found X");
        Assert.Contains("--- BEGIN QA FINDINGS ---", prompt);
        Assert.Contains("--- END QA FINDINGS ---", prompt);
        Assert.Contains("QA found X", prompt);
    }

    [Fact]
    public void Build_NullQaFindings_ContainsFallbackText()
    {
        var prompt = ReviewerPromptBuilder.Build("task", @"C:\ws", "proj", null);
        Assert.Contains("No QA findings", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ContainsReadOnlyConstraint()
    {
        var prompt = ReviewerPromptBuilder.Build("task", @"C:\ws", "proj", null);
        Assert.Contains("READ-ONLY", prompt);
    }

    [Fact]
    public void Build_ContainsReviewApprovedOutputFormat()
    {
        var prompt = ReviewerPromptBuilder.Build("task", @"C:\ws", "proj", null);
        Assert.Contains("REVIEW_APPROVED", prompt);
    }

    [Fact]
    public void Build_ContainsReviewChangesRequestedOutputFormat()
    {
        var prompt = ReviewerPromptBuilder.Build("task", @"C:\ws", "proj", null);
        Assert.Contains("REVIEW_CHANGES_REQUESTED", prompt);
    }

    [Fact]
    public void Build_SystemInstructionsBeforeTaskData()
    {
        var prompt = ReviewerPromptBuilder.Build("desc", @"C:\ws", "proj", null);
        var sysIdx = prompt.IndexOf("[SYSTEM INSTRUCTIONS", StringComparison.Ordinal);
        var dataIdx = prompt.IndexOf("--- BEGIN TASK DATA ---", StringComparison.Ordinal);
        Assert.True(sysIdx < dataIdx);
    }

    [Fact]
    public void Build_TaskDataBeforeQaFindings()
    {
        var prompt = ReviewerPromptBuilder.Build("task desc", @"C:\ws", "proj", "qa findings");
        var dataIdx = prompt.IndexOf("--- BEGIN TASK DATA ---", StringComparison.Ordinal);
        var qaIdx = prompt.IndexOf("--- BEGIN QA FINDINGS ---", StringComparison.Ordinal);
        Assert.True(dataIdx < qaIdx);
    }
}
