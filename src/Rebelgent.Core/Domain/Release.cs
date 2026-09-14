namespace Rebelgent.Core.Domain;

/// <summary>Represents a prepared or published GitHub release for a merged agent task.</summary>
public class Release
{
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public string Version { get; private set; }
    public string Title { get; private set; }
    public string Notes { get; private set; }
    public bool HasBreakingChanges { get; private set; }
    public ReleaseStatus Status { get; private set; }
    public string TagName { get; private set; }
    public string? GitHubReleaseUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public string MergeCommitSha { get; private set; }

    public Release(Guid taskId, string version, string title, string notes, bool hasBreakingChanges, string mergeCommitSha)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be empty.", nameof(version));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));
        if (notes is null)
            throw new ArgumentNullException(nameof(notes));
        if (string.IsNullOrWhiteSpace(mergeCommitSha))
            throw new ArgumentException("Merge commit SHA cannot be empty.", nameof(mergeCommitSha));

        Id = Guid.NewGuid();
        TaskId = taskId;
        Version = version;
        Title = title;
        Notes = notes;
        HasBreakingChanges = hasBreakingChanges;
        MergeCommitSha = mergeCommitSha;
        Status = ReleaseStatus.Prepared;
        TagName = $"v{version}";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    internal static Release Reconstitute(
        Guid id, Guid taskId, string version, string title, string notes,
        bool hasBreakingChanges, ReleaseStatus status, string tagName,
        string? gitHubReleaseUrl, DateTimeOffset createdAt, DateTimeOffset? publishedAt,
        string mergeCommitSha)
    {
        return new Release
        {
            Id = id,
            TaskId = taskId,
            Version = version,
            Title = title,
            Notes = notes,
            HasBreakingChanges = hasBreakingChanges,
            Status = status,
            TagName = tagName,
            GitHubReleaseUrl = gitHubReleaseUrl,
            CreatedAt = createdAt,
            PublishedAt = publishedAt,
            MergeCommitSha = mergeCommitSha
        };
    }

    private Release()
    {
        Version = string.Empty;
        Title = string.Empty;
        Notes = string.Empty;
        TagName = string.Empty;
        MergeCommitSha = string.Empty;
    }

    public void Approve()
    {
        if (Status != ReleaseStatus.Prepared)
            throw new InvalidOperationException($"Cannot approve a release in status '{Status}'.");
        Status = ReleaseStatus.Approved;
    }

    public void SetPublished(string gitHubReleaseUrl)
    {
        if (string.IsNullOrWhiteSpace(gitHubReleaseUrl))
            throw new ArgumentException("GitHub release URL cannot be empty.", nameof(gitHubReleaseUrl));
        GitHubReleaseUrl = gitHubReleaseUrl;
        PublishedAt = DateTimeOffset.UtcNow;
        Status = ReleaseStatus.Published;
    }

    public void SetFailed()
    {
        Status = ReleaseStatus.Failed;
    }
}
