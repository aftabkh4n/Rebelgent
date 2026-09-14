using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="Release"/>.</summary>
internal class ReleaseDbRecord
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool HasBreakingChanges { get; set; }
    public ReleaseStatus Status { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string? GitHubReleaseUrl { get; set; }
    public long CreatedAt { get; set; }
    public long? PublishedAt { get; set; }
    public string MergeCommitSha { get; set; } = string.Empty;

    public static ReleaseDbRecord FromDomain(Release release) => new()
    {
        Id = release.Id,
        TaskId = release.TaskId,
        Version = release.Version,
        Title = release.Title,
        Notes = release.Notes,
        HasBreakingChanges = release.HasBreakingChanges,
        Status = release.Status,
        TagName = release.TagName,
        GitHubReleaseUrl = release.GitHubReleaseUrl,
        CreatedAt = release.CreatedAt.UtcTicks,
        PublishedAt = release.PublishedAt?.UtcTicks,
        MergeCommitSha = release.MergeCommitSha
    };

    public Release ToDomain() => Release.Reconstitute(
        Id, TaskId, Version, Title, Notes, HasBreakingChanges, Status, TagName,
        GitHubReleaseUrl,
        new DateTimeOffset(CreatedAt, TimeSpan.Zero),
        PublishedAt.HasValue ? new DateTimeOffset(PublishedAt.Value, TimeSpan.Zero) : null,
        MergeCommitSha);
}
