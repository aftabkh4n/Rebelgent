using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="Package"/>.</summary>
internal class PackageDbRecord
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public PackageStatus Status { get; set; }
    public long PreparedAt { get; set; }
    public long? PublishedAt { get; set; }

    public static PackageDbRecord FromDomain(Package package) => new()
    {
        Id = package.Id,
        TaskId = package.TaskId,
        PackageId = package.PackageId,
        PackageVersion = package.PackageVersion,
        PackagePath = package.PackagePath,
        Status = package.Status,
        PreparedAt = package.PreparedAt.UtcTicks,
        PublishedAt = package.PublishedAt?.UtcTicks
    };

    public Package ToDomain() => Package.Reconstitute(
        Id, TaskId, PackageId, PackageVersion, PackagePath, Status,
        new DateTimeOffset(PreparedAt, TimeSpan.Zero),
        PublishedAt.HasValue ? new DateTimeOffset(PublishedAt.Value, TimeSpan.Zero) : null);
}
