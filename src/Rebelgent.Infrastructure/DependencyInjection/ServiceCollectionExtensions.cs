using Microsoft.Extensions.DependencyInjection;
using Rebelgent.Agents;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;

namespace Rebelgent.Infrastructure.DependencyInjection;

/// <summary>Extension methods for registering Rebelgent services with the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers core Rebelgent domain and application services.</summary>
    public static IServiceCollection AddRebelgent(this IServiceCollection services)
    {
        services.AddSingleton<IAgentRegistry, AgentRegistry>();
        services.AddSingleton<TaskLifecycleService>();
        services.AddScoped<ITaskService, TaskService>();
        return services;
    }
}
