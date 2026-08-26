using Microsoft.Extensions.Options;

namespace Rebelgent.Orchestration.Projects;

/// <summary>Configuration-backed implementation of <see cref="IProjectRegistry"/>.</summary>
internal sealed class ProjectRegistry : IProjectRegistry
{
    private readonly IReadOnlyDictionary<string, ProjectDefinition> _index;

    public ProjectRegistry(IOptions<ProjectRegistryOptions> options)
    {
        _index = options.Value.Projects
            .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ProjectDefinition> GetAll() => _index.Values.ToList().AsReadOnly();

    public ProjectDefinition? Find(string projectId) =>
        _index.TryGetValue(projectId, out var def) ? def : null;
}
