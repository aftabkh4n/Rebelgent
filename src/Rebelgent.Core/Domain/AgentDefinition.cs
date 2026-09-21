using Rebelgent.Core.Authority;

namespace Rebelgent.Core.Domain;

/// <summary>
/// Defines an AI agent — its identity, role, and lifecycle state.
/// Lifecycle transitions require a <see cref="HumanPrincipal"/> with the appropriate capability.
/// There is no delete — retired agents are permanently retained in the audit trail.
/// </summary>
public sealed class AgentDefinition
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public AgentRole Role { get; private set; }
    public string Purpose { get; private set; }
    public string Description { get; private set; }
    public AgentLifecycleStatus Status { get; private set; }
    public Guid? CurrentVersionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByHumanId { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }

    public AgentDefinition(
        string name,
        AgentRole role,
        string purpose,
        string description,
        Guid createdByHumanId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Purpose cannot be empty.", nameof(purpose));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty.", nameof(description));
        if (createdByHumanId == Guid.Empty)
            throw new ArgumentException("CreatedByHumanId cannot be empty.", nameof(createdByHumanId));

        Id = Guid.NewGuid();
        Name = name;
        Role = role;
        Purpose = purpose;
        Description = description;
        CreatedByHumanId = createdByHumanId;
        Status = AgentLifecycleStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    internal static AgentDefinition Reconstitute(
        Guid id, string name, AgentRole role, string purpose, string description,
        AgentLifecycleStatus status, Guid? currentVersionId, DateTimeOffset createdAt,
        Guid createdByHumanId, DateTimeOffset? activatedAt, DateTimeOffset? suspendedAt, DateTimeOffset? retiredAt)
    {
        return new AgentDefinition
        {
            Id = id,
            Name = name,
            Role = role,
            Purpose = purpose,
            Description = description,
            Status = status,
            CurrentVersionId = currentVersionId,
            CreatedAt = createdAt,
            CreatedByHumanId = createdByHumanId,
            ActivatedAt = activatedAt,
            SuspendedAt = suspendedAt,
            RetiredAt = retiredAt
        };
    }

    private AgentDefinition()
    {
        Name = string.Empty;
        Purpose = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Transitions the agent to Active status. Requires AwaitingApproval status.</summary>
    public void Activate(HumanPrincipal human)
    {
        ArgumentNullException.ThrowIfNull(human);
        if (Status != AgentLifecycleStatus.AwaitingApproval)
            throw new InvalidOperationException($"Cannot activate agent from status '{Status}'. Agent must be in AwaitingApproval status.");

        Status = AgentLifecycleStatus.Active;
        ActivatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Suspends an active agent.</summary>
    public void Suspend(HumanPrincipal human)
    {
        ArgumentNullException.ThrowIfNull(human);
        if (Status != AgentLifecycleStatus.Active)
            throw new InvalidOperationException($"Cannot suspend agent from status '{Status}'. Agent must be Active.");

        Status = AgentLifecycleStatus.Suspended;
        SuspendedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Permanently retires an agent. There is no un-retire.</summary>
    public void Retire(HumanPrincipal human)
    {
        ArgumentNullException.ThrowIfNull(human);
        if (Status is AgentLifecycleStatus.Retired)
            throw new InvalidOperationException("Agent is already retired.");

        Status = AgentLifecycleStatus.Retired;
        RetiredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Sets the current active version. Only callable when Active.</summary>
    public void SetCurrentVersion(Guid versionId)
    {
        if (Status != AgentLifecycleStatus.Active)
            throw new InvalidOperationException($"Cannot set current version when agent status is '{Status}'.");
        if (versionId == Guid.Empty)
            throw new ArgumentException("VersionId cannot be empty.", nameof(versionId));

        CurrentVersionId = versionId;
    }
}
