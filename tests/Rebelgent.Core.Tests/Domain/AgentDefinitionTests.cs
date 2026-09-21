using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Tests.Domain;

public class AgentDefinitionTests
{
    private static HumanPrincipal MakePrincipal() =>
        new(Guid.NewGuid(), "Test", "user-1",
            Enum.GetValues<HumanCapability>().ToHashSet(),
            DateTimeOffset.UtcNow);

    private static AgentDefinition MakeDraftAgent() =>
        new("TestAgent", AgentRole.BackendDeveloper,
            "Implements backend features",
            "A developer agent for backend tasks",
            Guid.NewGuid());

    private static AgentDefinition MakeAwaitingApprovalAgent()
    {
        var agent = new AgentDefinition("TestAgent", AgentRole.BackendDeveloper,
            "Implements backend features",
            "A developer agent for backend tasks",
            Guid.NewGuid());
        // Use Reconstitute to put it in AwaitingApproval status
        return AgentDefinition.Reconstitute(
            agent.Id, agent.Name, agent.Role, agent.Purpose, agent.Description,
            AgentLifecycleStatus.AwaitingApproval, null,
            agent.CreatedAt, agent.CreatedByHumanId, null, null, null);
    }

    private static AgentDefinition MakeActiveAgent()
    {
        var agent = MakeAwaitingApprovalAgent();
        agent.Activate(MakePrincipal());
        return agent;
    }

    [Fact]
    public void Constructor_ValidArguments_CreatesDraftAgent()
    {
        var humanId = Guid.NewGuid();
        var agent = new AgentDefinition("MyAgent", AgentRole.BackendDeveloper,
            "Does backend work", "A developer agent", humanId);

        Assert.NotEqual(Guid.Empty, agent.Id);
        Assert.Equal("MyAgent", agent.Name);
        Assert.Equal(AgentRole.BackendDeveloper, agent.Role);
        Assert.Equal(AgentLifecycleStatus.Draft, agent.Status);
        Assert.Equal(humanId, agent.CreatedByHumanId);
        Assert.Null(agent.ActivatedAt);
        Assert.Null(agent.SuspendedAt);
        Assert.Null(agent.RetiredAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyName_ThrowsArgumentException(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new AgentDefinition(name, AgentRole.BackendDeveloper, "Purpose", "Desc", Guid.NewGuid()));
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void Constructor_EmptyCreatedByHumanId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new AgentDefinition("Agent", AgentRole.BackendDeveloper, "Purpose", "Desc", Guid.Empty));
        Assert.Contains("CreatedByHumanId", ex.Message);
    }

    [Fact]
    public void Activate_WhenAwaitingApproval_TransitionsToActive()
    {
        var agent = MakeAwaitingApprovalAgent();
        var human = MakePrincipal();

        agent.Activate(human);

        Assert.Equal(AgentLifecycleStatus.Active, agent.Status);
        Assert.NotNull(agent.ActivatedAt);
    }

    [Fact]
    public void Activate_WhenDraft_ThrowsInvalidOperationException()
    {
        var agent = MakeDraftAgent();

        var ex = Assert.Throws<InvalidOperationException>(() => agent.Activate(MakePrincipal()));
        Assert.Contains("AwaitingApproval", ex.Message);
    }

    [Fact]
    public void Suspend_WhenActive_TransitionsToSuspended()
    {
        var agent = MakeActiveAgent();
        var human = MakePrincipal();

        agent.Suspend(human);

        Assert.Equal(AgentLifecycleStatus.Suspended, agent.Status);
        Assert.NotNull(agent.SuspendedAt);
    }

    [Fact]
    public void Retire_WhenActive_TransitionsToRetired()
    {
        var agent = MakeActiveAgent();
        var human = MakePrincipal();

        agent.Retire(human);

        Assert.Equal(AgentLifecycleStatus.Retired, agent.Status);
        Assert.NotNull(agent.RetiredAt);
    }
}
