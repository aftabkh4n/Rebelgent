namespace Rebelgent.Core.Authority;

/// <summary>Enforces human-only capability requirements. Throws if the principal is null or lacks the required capability.</summary>
public interface IHumanAuthorizationService
{
    void RequireHuman(HumanPrincipal? principal, HumanCapability capability, string action, string resourceId = "");
    void RequireHuman(HumanPrincipal? principal, HumanCapability capability, string action, Guid resourceId);
}
