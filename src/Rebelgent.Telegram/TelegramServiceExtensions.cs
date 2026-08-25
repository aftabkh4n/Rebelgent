using Microsoft.Extensions.DependencyInjection;
using Rebelgent.Telegram.Authorization;
using Rebelgent.Telegram.Handlers;
using Rebelgent.Telegram.Messaging;
using Rebelgent.Telegram.Services;

namespace Rebelgent.Telegram;

/// <summary>Extension methods for registering Telegram integration services.</summary>
public static class TelegramServiceExtensions
{
    /// <summary>
    /// Registers Telegram bot services. The bot only polls if Telegram:Enabled=true
    /// and Telegram:BotToken is configured; otherwise the API continues running normally.
    /// </summary>
    public static IServiceCollection AddRebelgentTelegram(this IServiceCollection services)
    {
        services.AddSingleton<TelegramAuthorizationService>();
        services.AddScoped<ITelegramMessageSender, TelegramMessageSender>();
        services.AddScoped<TelegramUpdateHandler>();
        services.AddHostedService<TelegramBotService>();
        return services;
    }
}
