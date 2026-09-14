using Rebelgent.Core.Domain;
using Rebelgent.GitHub.Package;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakePackageOrchestrator : IPackageOrchestrator
{
    public PackageOrchestratorResult PrepareResult { get; set; } = new()
    {
        Succeeded = true,
        PackageId = "MyLib",
        PackageVersion = "1.0.0",
        PackagePath = @"C:\temp\MyLib.1.0.0.nupkg",
        Status = PackageStatus.Prepared,
        PreparedAt = DateTimeOffset.UtcNow,
        Summary = "Package prepared: MyLib 1.0.0"
    };

    public PackageOrchestratorResult ApproveResult { get; set; } = new()
    {
        Succeeded = true,
        PackageId = "MyLib",
        PackageVersion = "1.0.0",
        PackagePath = @"C:\temp\MyLib.1.0.0.nupkg",
        Status = PackageStatus.Published,
        PreparedAt = DateTimeOffset.UtcNow,
        PublishedAt = DateTimeOffset.UtcNow,
        Summary = "Package published: MyLib 1.0.0"
    };

    public PackageOrchestratorResult InfoResult { get; set; } = new()
    {
        Succeeded = true,
        PackageId = "MyLib",
        PackageVersion = "1.0.0",
        PackagePath = @"C:\temp\MyLib.1.0.0.nupkg",
        Status = PackageStatus.Prepared,
        PreparedAt = DateTimeOffset.UtcNow,
        Summary = "Package: MyLib 1.0.0 — Prepared"
    };

    public Guid? LastPrepareTaskId { get; private set; }
    public Guid? LastApproveTaskId { get; private set; }
    public Guid? LastInfoTaskId { get; private set; }

    public Task<PackageOrchestratorResult> PrepareAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        LastPrepareTaskId = taskId;
        return Task.FromResult(PrepareResult);
    }

    public Task<PackageOrchestratorResult> ApproveAndPublishAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        LastApproveTaskId = taskId;
        return Task.FromResult(ApproveResult);
    }

    public Task<PackageOrchestratorResult> GetInfoAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        LastInfoTaskId = taskId;
        return Task.FromResult(InfoResult);
    }
}
