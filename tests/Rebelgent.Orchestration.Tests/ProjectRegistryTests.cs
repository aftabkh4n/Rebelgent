using Rebelgent.Orchestration.Projects;

namespace Rebelgent.Orchestration.Tests;

public class ProjectRegistryTests
{
    private static IProjectRegistry BuildRegistry(params ProjectDefinition[] definitions)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ProjectRegistryOptions { Projects = [.. definitions] });
        return new ProjectRegistry(options);
    }

    [Fact]
    public void GetAll_WithNoProjects_ReturnsEmpty()
    {
        var registry = BuildRegistry();
        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void GetAll_WithProjects_ReturnsAll()
    {
        var registry = BuildRegistry(
            new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = @"D:\Projects\Sandbox", DefaultBranch = "main" },
            new ProjectDefinition { Id = "demo", Name = "Demo", RepositoryPath = @"D:\Projects\Demo", DefaultBranch = "main" });

        Assert.Equal(2, registry.GetAll().Count);
    }

    [Fact]
    public void Find_ExistingId_ReturnsDefinition()
    {
        var registry = BuildRegistry(
            new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = @"D:\Projects\Sandbox", DefaultBranch = "main" });

        var result = registry.Find("sandbox");

        Assert.NotNull(result);
        Assert.Equal("sandbox", result.Id);
    }

    [Fact]
    public void Find_CaseInsensitive_ReturnsDefinition()
    {
        var registry = BuildRegistry(
            new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = @"D:\Projects\Sandbox", DefaultBranch = "main" });

        Assert.NotNull(registry.Find("SANDBOX"));
        Assert.NotNull(registry.Find("Sandbox"));
    }

    [Fact]
    public void Find_NonExistingId_ReturnsNull()
    {
        var registry = BuildRegistry();
        Assert.Null(registry.Find("nonexistent"));
    }

    [Fact]
    public void Find_EmptyRegistry_ReturnsNull()
    {
        var registry = BuildRegistry();
        Assert.Null(registry.Find("sandbox"));
    }
}
