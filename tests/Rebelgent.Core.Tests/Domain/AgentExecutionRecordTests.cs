using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Tests.Domain;

public class AgentExecutionRecordTests
{
    private static AgentExecutionRecord MakeRecord() =>
        new(Guid.NewGuid(), "proj", @"C:\ws\repo", "rebelgent/task-abc12345");

    [Fact]
    public void CommitSha_InitiallyNull()
    {
        var record = MakeRecord();
        Assert.Null(record.CommitSha);
    }

    [Fact]
    public void SetCommitSha_StoresValue()
    {
        var record = MakeRecord();
        record.SetCommitSha("abc1234567890def");
        Assert.Equal("abc1234567890def", record.CommitSha);
    }

    [Fact]
    public void SetCommitSha_AfterComplete_IsPreserved()
    {
        var record = MakeRecord();
        record.MarkRunning();
        record.Complete("output", "build ok", "tests ok", true, true);
        record.SetCommitSha("deadbeef123");

        Assert.Equal("deadbeef123", record.CommitSha);
        Assert.Equal(ExecutionStatus.Succeeded, record.Status);
    }

    [Fact]
    public void SetCommitSha_CanBeOverwritten()
    {
        var record = MakeRecord();
        record.SetCommitSha("first");
        record.SetCommitSha("second");
        Assert.Equal("second", record.CommitSha);
    }
}
