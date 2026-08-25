namespace Rebelgent.Telegram.Options;

/// <summary>Configuration options for the Telegram bot integration.</summary>
public class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Set to true to start the Telegram bot. Defaults to false so the API can run without a token configured.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Telegram bot token from BotFather. Never commit this value to source control.</summary>
    public string? BotToken { get; set; }

    /// <summary>
    /// Numeric Telegram user IDs that may interact with this bot.
    /// Only numeric IDs are trusted — usernames are not sufficient for authorization.
    /// </summary>
    public List<long> AllowedUserIds { get; set; } = [];
}
