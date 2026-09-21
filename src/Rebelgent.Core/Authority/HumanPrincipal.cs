namespace Rebelgent.Core.Authority;

/// <summary>
/// Represents an authenticated human actor. Only creatable via <see cref="IHumanPrincipalFactory"/>.
/// Cannot be constructed from arbitrary strings — requires explicit identity provider and external ID.
/// </summary>
public sealed record HumanPrincipal
{
    public Guid HumanId { get; init; }
    public string IdentityProvider { get; init; }
    public string ExternalIdentityId { get; init; }
    public IReadOnlySet<HumanCapability> Capabilities { get; init; }
    public DateTimeOffset AuthenticatedAt { get; init; }

    public HumanPrincipal(
        Guid humanId,
        string identityProvider,
        string externalIdentityId,
        IReadOnlySet<HumanCapability> capabilities,
        DateTimeOffset authenticatedAt)
    {
        if (humanId == Guid.Empty)
            throw new ArgumentException("HumanId cannot be empty.", nameof(humanId));
        if (string.IsNullOrWhiteSpace(identityProvider))
            throw new ArgumentException("IdentityProvider cannot be empty.", nameof(identityProvider));
        if (string.IsNullOrWhiteSpace(externalIdentityId))
            throw new ArgumentException("ExternalIdentityId cannot be empty.", nameof(externalIdentityId));
        ArgumentNullException.ThrowIfNull(capabilities);

        HumanId = humanId;
        IdentityProvider = identityProvider;
        ExternalIdentityId = externalIdentityId;
        Capabilities = capabilities;
        AuthenticatedAt = authenticatedAt;
    }

    public bool HasCapability(HumanCapability capability) => Capabilities.Contains(capability);
}
