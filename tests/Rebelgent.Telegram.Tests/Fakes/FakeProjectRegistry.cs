using Rebelgent.Orchestration.Projects;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeProjectRegistry : IProjectRegistry
{
    private readonly List<ProjectDefinition> _projects;

    public FakeProjectRegistry(IEnumerable<ProjectDefinition>? projects = null)
    {
        _projects = projects?.ToList() ?? [];
    }

    public IReadOnlyCollection<ProjectDefinition> GetAll() => _projects.AsReadOnly();

    public ProjectDefinition? Find(string projectId) =>
        _projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
}
