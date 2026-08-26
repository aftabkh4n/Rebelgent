using Microsoft.Extensions.DependencyInjection;
using Rebelgent.Orchestration.Orchestrator;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.Orchestration.DependencyInjection;

/// <summary>Registers orchestration services.</summary>
public static class OrchestrationServiceExtensions
{
    public static IServiceCollection AddRebelgentOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<IProjectRegistry, ProjectRegistry>();
        services.AddSingleton<IWorkspaceManager, GitWorkspaceManager>();
        services.AddSingleton<ExecutionConcurrencyGuard>();
        services.AddSingleton<ITaskOrchestrator, TaskOrchestrator>();

        return services;
    }
}
