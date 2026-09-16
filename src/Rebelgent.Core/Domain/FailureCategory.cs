namespace Rebelgent.Core.Domain;

/// <summary>Taxonomy of categorized execution failures used for self-improvement analysis.</summary>
public enum FailureCategory
{
    BuildFailure,
    TestFailure,
    QaRejection,
    ReviewerRejection,
    ProcessFailure,
    Timeout,
    WorkspaceFailure,
    GitFailure,
    GitHubFailure,
    ReleaseFailure,
    PackageFailure,
    AgentFailure,
    Unknown
}
