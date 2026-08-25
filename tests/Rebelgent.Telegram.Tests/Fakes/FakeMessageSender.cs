using Rebelgent.Telegram.Messaging;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeMessageSender : ITelegramMessageSender
{
    public List<(long ChatId, string Text)> SentMessages { get; } = [];

    public Task SendTextAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        SentMessages.Add((chatId, text));
        return Task.CompletedTask;
    }
}
