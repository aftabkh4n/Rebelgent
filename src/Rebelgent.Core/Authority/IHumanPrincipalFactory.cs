namespace Rebelgent.Core.Authority;

/// <summary>Factory for creating authenticated <see cref="HumanPrincipal"/> instances.</summary>
public interface IHumanPrincipalFactory
{
    HumanPrincipal Create(string identityProvider, string externalIdentityId, IReadOnlySet<HumanCapability> capabilities);
}
