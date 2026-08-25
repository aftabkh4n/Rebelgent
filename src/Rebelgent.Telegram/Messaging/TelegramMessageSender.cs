using Microsoft.Extensions.Options;
using Rebelgent.Telegram.Options;
using Telegram.Bot;

namespace Rebelgent.Telegram.Messaging;

/// <summary>Sends outbound messages via the Telegram Bot API.</summary>
internal class TelegramMessageSender : ITelegramMessageSender
{
    private readonly IOptions<TelegramOptions> _options;

    public TelegramMessageSender(IOptions<TelegramOptions> options)
    {
        _options = options;
    }

    public async Task SendTextAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        var token = _options.Value.BotToken;
        if (string.IsNullOrWhiteSpace(token))
            return;
        var client = new TelegramBotClient(token);
        await client.SendMessage(chatId, text, cancellationToken: cancellationToken);
    }
}
