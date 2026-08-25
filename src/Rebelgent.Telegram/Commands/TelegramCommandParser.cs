namespace Rebelgent.Telegram.Commands;

/// <summary>
/// Parses and normalizes Telegram bot commands.
/// Handles both /command and /command@botname formats (the latter is sent by
/// Telegram clients when using the command menu, even in private chats).
/// </summary>
internal static class TelegramCommandParser
{
    /// <summary>
    /// Attempts to parse a normalized command from message text.
    /// Returns true when the text is a slash command; <paramref name="command"/> is
    /// the lowercase command name including the leading slash (e.g. "/status").
    /// Returns false when the text is ordinary (non-command) input.
    /// </summary>
    public static bool TryParse(string text, out string command)
    {
        var trimmed = text.Trim();

        if (!trimmed.StartsWith('/') || trimmed.Length < 2)
        {
            command = string.Empty;
            return false;
        }

        // Take only the command token — everything before the first space.
        var token = trimmed.Split(' ')[0].ToLowerInvariant();

        // Strip @botname suffix that Telegram appends in group chats and command-menu usage.
        var atIndex = token.IndexOf('@');
        command = atIndex > 0 ? token[..atIndex] : token;

        return true;
    }
}
