namespace Rebelgent.Orchestration.Projects;

/// <summary>Configuration section that enumerates allowed projects.</summary>
public sealed class ProjectRegistryOptions
{
    public const string SectionName = "Projects";

    public List<ProjectDefinition> Projects { get; set; } = [];
}
