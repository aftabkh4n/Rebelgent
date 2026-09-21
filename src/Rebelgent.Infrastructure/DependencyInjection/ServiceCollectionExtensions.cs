using Microsoft.Extensions.DependencyInjection;
using Rebelgent.Agents;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Observability;
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
        // No exporter is wired up by default — Rebelgent runs fully locally at $0.
        // Swap in a future Langfuse/OpenTelemetry adapter project here if one is added.
        services.AddSingleton<IObservabilityExporter, NullObservabilityExporter>();
        services.AddScoped<IExecutionAnalysisService, ExecutionAnalysisService>();
        services.AddScoped<IMetricsCalculator, MetricsCalculator>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IHumanAuthorizationService, HumanAuthorizationService>();
        services.AddScoped<IAgentLifecycleService, AgentLifecycleService>();
        services.AddScoped<IAuditLedgerVerifier, AuditLedgerVerifier>();
        services.AddScoped<IAuditRecoveryService, AuditRecoveryService>();
        services.AddSingleton<IScopedBackgroundExecutor, ScopedBackgroundExecutor>();
        services.AddScoped<IBuiltInAgentBootstrapper, BuiltInAgentBootstrapper>();
        return services;
    }
}
