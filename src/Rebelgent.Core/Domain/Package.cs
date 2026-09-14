namespace Rebelgent.Core.Domain;

/// <summary>Represents a NuGet package prepared or published from a merged and released agent task.</summary>
public class Package
{
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public string PackageId { get; private set; }
    public string PackageVersion { get; private set; }

    /// <summary>Absolute path to the .nupkg file on this host.</summary>
    public string PackagePath { get; private set; }

    public PackageStatus Status { get; private set; }
    public DateTimeOffset PreparedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public Package(Guid taskId, string packageId, string packageVersion, string packagePath)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(packageVersion))
            throw new ArgumentException("Package version cannot be empty.", nameof(packageVersion));
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path cannot be empty.", nameof(packagePath));

        Id = Guid.NewGuid();
        TaskId = taskId;
        PackageId = packageId;
        PackageVersion = packageVersion;
        PackagePath = packagePath;
        Status = PackageStatus.Prepared;
        PreparedAt = DateTimeOffset.UtcNow;
    }

    internal static Package Reconstitute(
        Guid id, Guid taskId, string packageId, string packageVersion, string packagePath,
        PackageStatus status, DateTimeOffset preparedAt, DateTimeOffset? publishedAt)
    {
        return new Package
        {
            Id = id,
            TaskId = taskId,
            PackageId = packageId,
            PackageVersion = packageVersion,
            PackagePath = packagePath,
            Status = status,
            PreparedAt = preparedAt,
            PublishedAt = publishedAt
        };
    }

    private Package()
    {
        PackageId = string.Empty;
        PackageVersion = string.Empty;
        PackagePath = string.Empty;
    }

    public void SetPublished()
    {
        if (Status != PackageStatus.Prepared)
            throw new InvalidOperationException($"Cannot publish a package in status '{Status}'.");
        Status = PackageStatus.Published;
        PublishedAt = DateTimeOffset.UtcNow;
    }

    public void SetFailed()
    {
        Status = PackageStatus.Failed;
    }
}
