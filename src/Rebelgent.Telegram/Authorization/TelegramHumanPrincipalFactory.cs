using System.Security.Cryptography;
using System.Text;
using Rebelgent.Core.Authority;

namespace Rebelgent.Telegram.Authorization;

/// <summary>
/// Creates <see cref="HumanPrincipal"/> instances from authenticated Telegram user IDs.
/// The identity provider is always "Telegram" and HumanId is deterministic from the
/// combination of identity provider and external ID so the same person gets the same ID
/// across sessions.
/// </summary>
public sealed class TelegramHumanPrincipalFactory : IHumanPrincipalFactory
{
    public HumanPrincipal Create(string identityProvider, string externalIdentityId, IReadOnlySet<HumanCapability> capabilities)
    {
        var humanId = DeterministicGuid(identityProvider, externalIdentityId);
        return new HumanPrincipal(humanId, identityProvider, externalIdentityId, capabilities, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a HumanPrincipal for an authorized Telegram user with all capabilities.
    /// The HumanId is deterministic so the same Telegram user ID always maps to the same GUID.
    /// </summary>
    public HumanPrincipal CreateFromTelegramUserId(long telegramUserId)
    {
        var allCapabilities = (IReadOnlySet<HumanCapability>)Enum.GetValues<HumanCapability>().ToHashSet();
        return Create("Telegram", telegramUserId.ToString(), allCapabilities);
    }

    private static Guid DeterministicGuid(string identityProvider, string externalId)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{identityProvider}:{externalId}"));
        return new Guid(bytes);
    }
}
