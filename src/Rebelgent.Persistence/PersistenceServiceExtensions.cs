using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Options;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence;

/// <summary>Extension methods for registering persistence services.</summary>
public static class PersistenceServiceExtensions
{
    /// <summary>Registers EF Core SQLite and repository implementations.</summary>
    public static IServiceCollection AddRebelgentPersistence(this IServiceCollection services)
    {
        services.AddDbContext<RebelgentDbContext>((sp, options) =>
        {
            var persistenceOptions = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value;
            options.UseSqlite(persistenceOptions.ConnectionString);
        });

        services.AddScoped<IAgentTaskRepository, AgentTaskRepository>();
        services.AddScoped<IAgentExecutionRepository, AgentExecutionRepository>();
        services.AddScoped<IReleaseRepository, EfReleaseRepository>();

        return services;
    }
}
