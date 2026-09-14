using Rebelgent.ClaudeCode;

namespace Rebelgent.ClaudeCode.Tests;

public class QaPromptBuilderTests
{
    [Fact]
    public void Build_ContainsSystemInstructions()
    {
        var prompt = QaPromptBuilder.Build("Implement search", @"C:\workspace\qa", "sandbox");
        Assert.Contains("[SYSTEM INSTRUCTIONS", prompt);
        Assert.Contains("[END SYSTEM INSTRUCTIONS]", prompt);
    }

    [Fact]
    public void Build_ContainsTaskDataDelimiters()
    {
        var prompt = QaPromptBuilder.Build("Implement search", @"C:\workspace\qa", "sandbox");
        Assert.Contains("--- BEGIN TASK DATA ---", prompt);
        Assert.Contains("--- END TASK DATA ---", prompt);
    }

    [Fact]
    public void Build_ContainsTaskDescription()
    {
        var prompt = QaPromptBuilder.Build("Implement search", @"C:\workspace\qa", "sandbox");
        Assert.Contains("Implement search", prompt);
    }

    [Fact]
    public void Build_SystemInstructionsBeforeTaskData()
    {
        var prompt = QaPromptBuilder.Build("task desc", @"C:\workspace\qa", "sandbox");
        var sysIdx = prompt.IndexOf("[SYSTEM INSTRUCTIONS", StringComparison.Ordinal);
        var dataIdx = prompt.IndexOf("--- BEGIN TASK DATA ---", StringComparison.Ordinal);
        Assert.True(sysIdx < dataIdx);
    }

    [Fact]
    public void Build_ContainsReadOnlyConstraint()
    {
        var prompt = QaPromptBuilder.Build("task", @"C:\ws", "proj");
        Assert.Contains("READ-ONLY", prompt);
    }

    [Fact]
    public void Build_ContainsQaPassedOutputFormat()
    {
        var prompt = QaPromptBuilder.Build("task", @"C:\ws", "proj");
        Assert.Contains("QA_PASSED", prompt);
    }

    [Fact]
    public void Build_ContainsQaFailedOutputFormat()
    {
        var prompt = QaPromptBuilder.Build("task", @"C:\ws", "proj");
        Assert.Contains("QA_FAILED", prompt);
    }

    [Fact]
    public void Build_ContainsProjectId()
    {
        var prompt = QaPromptBuilder.Build("task", @"C:\ws", "my-project");
        Assert.Contains("my-project", prompt);
    }

    [Fact]
    public void Build_TaskDescriptionIsInsideDelimiters()
    {
        var prompt = QaPromptBuilder.Build("unique-task-xyz", @"C:\ws", "proj");
        var beginIdx = prompt.IndexOf("--- BEGIN TASK DATA ---", StringComparison.Ordinal);
        var endIdx = prompt.IndexOf("--- END TASK DATA ---", StringComparison.Ordinal);
        var taskIdx = prompt.IndexOf("unique-task-xyz", StringComparison.Ordinal);
        Assert.True(taskIdx > beginIdx && taskIdx < endIdx);
    }
}
