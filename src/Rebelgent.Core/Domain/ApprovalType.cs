namespace Rebelgent.Core.Domain;

/// <summary>Identifies the category of action requiring human approval.</summary>
public enum ApprovalType
{
    Architecture,
    Merge,
    Deployment,
    PackagePublish,
    ContentPublish,
    DestructiveAction,
    SecretChange
}
