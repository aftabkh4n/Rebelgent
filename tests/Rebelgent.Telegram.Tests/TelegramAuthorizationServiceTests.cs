using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rebelgent.Telegram.Authorization;
using TelegramOptions = Rebelgent.Telegram.Options.TelegramOptions;

namespace Rebelgent.Telegram.Tests;

public class TelegramAuthorizationServiceTests
{
    private TelegramAuthorizationService Build(params long[] allowedIds)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [.. allowedIds] });
        return new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
    }

    [Fact]
    public void AllowedUserId_IsAuthorized()
    {
        var service = Build(12345678L);
        Assert.True(service.IsAuthorized(12345678L));
    }

    [Fact]
    public void UnknownUserId_IsNotAuthorized()
    {
        var service = Build(12345678L);
        Assert.False(service.IsAuthorized(99999999L));
    }

    [Fact]
    public void EmptyAllowedList_NobodyIsAuthorized()
    {
        var service = Build();
        Assert.False(service.IsAuthorized(12345678L));
    }

    [Fact]
    public void MultipleAllowedIds_OnlyMatchingIsAuthorized()
    {
        var service = Build(111L, 222L, 333L);
        Assert.True(service.IsAuthorized(222L));
        Assert.False(service.IsAuthorized(444L));
    }
}
