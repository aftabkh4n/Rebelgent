namespace Rebelgent.Telegram.Messaging;

/// <summary>
/// Abstraction over outbound Telegram messaging.
/// Allows tests to verify sent messages without making real API calls.
/// </summary>
public interface ITelegramMessageSender
{
    Task SendTextAsync(long chatId, string text, CancellationToken cancellationToken = default);
}
