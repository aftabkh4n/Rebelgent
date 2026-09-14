using Microsoft.Extensions.DependencyInjection;
using Rebelgent.ClaudeCode.DependencyInjection;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.Infrastructure.DependencyInjection;
using Rebelgent.Orchestration.DependencyInjection;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Persistence;
using Rebelgent.Persistence.Options;
using Rebelgent.Telegram.Handlers;
using Rebelgent.Telegram.Options;

namespace Rebelgent.Telegram.Tests;

public class TelegramDiCompositionTests
{
    private static IServiceCollection BuildServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services.Configure<PersistenceOptions>(opts =>
            opts.ConnectionString = "Data Source=:memory:");

        services.Configure<TelegramOptions>(opts =>
        {
            opts.Enabled = false;
            opts.BotToken = null;
            opts.AllowedUserIds = [];
        });

        services.Configure<WorkspaceOptions>(opts =>
            opts.RootPath = @"D:\Projects\_RebelgentWorkspaces");

        services.Configure<ExecutionOptions>(_ => { });

        services.Configure<ProjectRegistryOptions>(opts =>
            opts.Projects = []);

        services.Configure<ClaudeCodeOptions>(_ => { });

        services.AddRebelgent();
        services.AddRebelgentPersistence();
        services.AddRebelgentOrchestration();
        services.AddRebelgentClaudeCode();
        services.AddRebelgentTelegram();

        return services;
    }

    [Fact]
    public void BuildServiceProvider_WithValidateOnBuild_DoesNotThrow()
    {
        var services = BuildServices();
        var exception = Record.Exception(() =>
            services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }));

        Assert.Null(exception);
    }

    [Fact]
    public void TelegramUpdateHandler_ResolvesFromScope()
    {
        var services = BuildServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>();

        Assert.NotNull(handler);
    }
}
