namespace Rebelgent.Core.Authority;

/// <summary>Thrown when an authorization check fails for a human principal.</summary>
public sealed class HumanAuthorizationException : Exception
{
    public ActorType ActorType { get; }
    public string AttemptedAction { get; }
    public string ResourceId { get; }
    public AuthorityViolationKind ViolationKind { get; }

    public HumanAuthorizationException(
        ActorType actorType,
        string attemptedAction,
        string resourceId,
        AuthorityViolationKind violationKind,
        string message)
        : base(message)
    {
        ActorType = actorType;
        AttemptedAction = attemptedAction;
        ResourceId = resourceId;
        ViolationKind = violationKind;
    }
}
