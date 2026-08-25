using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.Telegram.Options;

namespace Rebelgent.Telegram.Authorization;

/// <summary>
/// Enforces that only configured numeric Telegram user IDs may interact with the bot.
/// Username-based authorization is explicitly not supported.
/// </summary>
public class TelegramAuthorizationService
{
    private readonly IReadOnlySet<long> _allowedUserIds;
    private readonly ILogger<TelegramAuthorizationService> _logger;

    public TelegramAuthorizationService(IOptions<TelegramOptions> options, ILogger<TelegramAuthorizationService> logger)
    {
        _allowedUserIds = options.Value.AllowedUserIds.ToHashSet();
        _logger = logger;
    }

    public bool IsAuthorized(long userId)
    {
        if (_allowedUserIds.Contains(userId))
            return true;

        _logger.LogWarning("Unauthorized Telegram access attempt from user ID {UserId}", userId);
        return false;
    }
}
