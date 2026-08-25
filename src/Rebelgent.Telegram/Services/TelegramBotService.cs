using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.Telegram.Handlers;
using Rebelgent.Telegram.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Rebelgent.Telegram.Services;

/// <summary>
/// Hosted service that runs the Telegram long-polling loop.
/// Checks configuration at startup; logs and exits cleanly if disabled or unconfigured.
/// Creates a DI scope per update so scoped services (e.g. DbContext) are handled correctly.
/// </summary>
internal class TelegramBotService : BackgroundService
{
    private readonly IOptions<TelegramOptions> _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramBotService> _logger;

    public TelegramBotService(
        IOptions<TelegramOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramBotService> logger)
    {
        _options = options;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;

        if (!opts.Enabled)
        {
            _logger.LogInformation("Telegram integration is disabled. Set Telegram:Enabled=true to enable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.BotToken))
        {
            _logger.LogWarning("Telegram integration is enabled but Telegram:BotToken is not configured. Bot will not start.");
            return;
        }

        _logger.LogInformation("Telegram integration started");

        var client = new TelegramBotClient(opts.BotToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message]
        };

        await client.ReceiveAsync(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Telegram integration stopped");
    }

    private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>();
        try
        {
            await handler.HandleAsync(update, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing Telegram update {UpdateId}", update.Id);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }
}
