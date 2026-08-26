namespace Rebelgent.Orchestration.Projects;

/// <summary>Read-only registry of projects agents are permitted to work on.</summary>
public interface IProjectRegistry
{
    IReadOnlyCollection<ProjectDefinition> GetAll();

    ProjectDefinition? Find(string projectId);
}
