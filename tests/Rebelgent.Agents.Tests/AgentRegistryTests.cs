using Rebelgent.Agents;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Exceptions;

namespace Rebelgent.Agents.Tests;

public class AgentRegistryTests
{
    private readonly AgentRegistry _registry = new();

    [Fact]
    public void Register_And_Resolve_ReturnsCorrectAgent()
    {
        var agent = new FakeAgent("Backend Bot", AgentRole.BackendDeveloper);
        _registry.Register(agent);

        var resolved = _registry.Resolve(AgentRole.BackendDeveloper);
        Assert.Same(agent, resolved);
    }

    [Fact]
    public void Register_DuplicateRole_Throws()
    {
        _registry.Register(new FakeAgent("Bot A", AgentRole.QaEngineer));

        var ex = Assert.Throws<DuplicateAgentRoleException>(() =>
            _registry.Register(new FakeAgent("Bot B", AgentRole.QaEngineer)));
        Assert.Equal(AgentRole.QaEngineer, ex.Role);
    }

    [Fact]
    public void Resolve_UnregisteredRole_Throws()
    {
        var ex = Assert.Throws<AgentNotFoundException>(() =>
            _registry.Resolve(AgentRole.SolutionArchitect));
        Assert.Equal(AgentRole.SolutionArchitect, ex.Role);
    }

    [Fact]
    public void GetAll_ReturnsAllRegisteredAgents()
    {
        var backend = new FakeAgent("Backend Bot", AgentRole.BackendDeveloper);
        var qa = new FakeAgent("QA Bot", AgentRole.QaEngineer);

        _registry.Register(backend);
        _registry.Register(qa);

        var all = _registry.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(backend, all);
        Assert.Contains(qa, all);
    }

    [Fact]
    public void GetAll_Empty_ReturnsEmptyCollection()
    {
        Assert.Empty(_registry.GetAll());
    }

    [Fact]
    public void Register_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _registry.Register(null!));
    }

    [Fact]
    public void Resolve_CorrectRole_AfterMultipleRegistrations()
    {
        _registry.Register(new FakeAgent("Frontend Bot", AgentRole.FrontendDeveloper));
        _registry.Register(new FakeAgent("DevOps Bot", AgentRole.DevOpsEngineer));
        _registry.Register(new FakeAgent("Architect Bot", AgentRole.SolutionArchitect));

        var resolved = _registry.Resolve(AgentRole.DevOpsEngineer);
        Assert.Equal("DevOps Bot", resolved.Name);
        Assert.Equal(AgentRole.DevOpsEngineer, resolved.Role);
    }

    private sealed class FakeAgent(string name, AgentRole role) : IRebelAgent
    {
        public string Name { get; } = name;
        public AgentRole Role { get; } = role;

        public Task<AgentExecutionResult> ExecuteAsync(
            AgentTask task,
            AgentExecutionContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(AgentExecutionResult.Succeeded("Done"));
        }
    }
}
