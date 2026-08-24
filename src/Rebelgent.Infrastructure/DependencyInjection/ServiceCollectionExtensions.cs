using Microsoft.Extensions.DependencyInjection;
using Rebelgent.Agents;
using Rebelgent.Core.Services;

namespace Rebelgent.Infrastructure.DependencyInjection;

/// <summary>Extension methods for registering Rebelgent services with the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the core Rebelgent services.</summary>
    public static IServiceCollection AddRebelgent(this IServiceCollection services)
    {
        services.AddSingleton<IAgentRegistry, AgentRegistry>();
        services.AddSingleton<TaskLifecycleService>();
        return services;
    }
}
