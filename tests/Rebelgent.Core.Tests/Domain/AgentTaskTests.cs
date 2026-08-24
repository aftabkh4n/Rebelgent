using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Tests.Domain;

public class AgentTaskTests
{
    [Fact]
    public void Constructor_ValidArguments_CreatesTask()
    {
        var task = new AgentTask("project-1", "Implement login", "Add user authentication", AgentRole.BackendDeveloper);

        Assert.Equal("project-1", task.ProjectId);
        Assert.Equal("Implement login", task.Title);
        Assert.Equal("Add user authentication", task.Description);
        Assert.Equal(AgentRole.BackendDeveloper, task.AssignedRole);
        Assert.Equal(AgentTaskStatus.Created, task.Status);
        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Null(task.StartedAt);
        Assert.Null(task.CompletedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyTitle_Throws(string title)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new AgentTask("project-1", title, "desc", AgentRole.BackendDeveloper));
        Assert.Contains("Title", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyProjectId_Throws(string projectId)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new AgentTask(projectId, "title", "desc", AgentRole.BackendDeveloper));
        Assert.Contains("Project ID", ex.Message);
    }

    [Fact]
    public void Constructor_NullDescription_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentTask("project-1", "title", null!, AgentRole.BackendDeveloper));
    }

    [Fact]
    public void Constructor_EmptyDescriptionAllowed()
    {
        var task = new AgentTask("project-1", "title", "", AgentRole.BackendDeveloper);
        Assert.Equal("", task.Description);
    }

    [Fact]
    public void SetBranchName_ValidName_SetsBranch()
    {
        var task = new AgentTask("project-1", "title", "desc", AgentRole.BackendDeveloper);
        task.SetBranchName("feature/login");
        Assert.Equal("feature/login", task.BranchName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetBranchName_Empty_Throws(string name)
    {
        var task = new AgentTask("project-1", "title", "desc", AgentRole.BackendDeveloper);
        Assert.Throws<ArgumentException>(() => task.SetBranchName(name));
    }

    [Fact]
    public void SetPullRequestNumber_Positive_Sets()
    {
        var task = new AgentTask("project-1", "title", "desc", AgentRole.BackendDeveloper);
        task.SetPullRequestNumber(42);
        Assert.Equal(42, task.PullRequestNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetPullRequestNumber_NonPositive_Throws(int number)
    {
        var task = new AgentTask("project-1", "title", "desc", AgentRole.BackendDeveloper);
        Assert.Throws<ArgumentOutOfRangeException>(() => task.SetPullRequestNumber(number));
    }
}
