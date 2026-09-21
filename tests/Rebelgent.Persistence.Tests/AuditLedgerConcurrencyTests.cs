using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Infrastructure.Services;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Proves that concurrent audit-event appends do not corrupt the hash chain.
/// Two writers must not obtain the same SequenceNumber or the same PreviousHash.
/// The <see cref="EfUnitOfWork"/> uses SQLite <c>BEGIN IMMEDIATE</c> (via
/// <see cref="System.Data.IsolationLevel.Serializable"/>), which serialises writers so the
/// GetLatest/Append pair is atomic.
/// </summary>
public class AuditLedgerConcurrencyTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAliveConnection;

    public AuditLedgerConcurrencyTests()
    {
        // Shared in-memory database — multiple connections see the same data.
        // The keep-alive connection prevents the in-memory DB from being torn down
        // when transient scope connections close.
        var dbName = $"concurrency-{Guid.NewGuid():N}";
        _connectionString = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var ctx = CreateContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
    }

    private RebelgentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new RebelgentDbContext(options);
    }

    private async Task RecordOne(int index)
    {
        using var ctx = CreateContext();
        var repo = new EfAuditRepository(ctx);
        var uow = new EfUnitOfWork(ctx);
        var service = new AuditService(repo, NullLogger<AuditService>.Instance);

        await uow.ExecuteInTransactionAsync(async ct =>
        {
            await service.RecordAsync(
                AuditEventType.TaskCreated,
                ActorType.System,
                $"writer-{index}",
                "AgentTask",
                Guid.NewGuid().ToString(),
                "Create",
                new { index },
                ct);
        });
    }

    [Fact]
    public async Task ConcurrentAppends_ProduceContiguousSequenceNumbersAndValidChain()
    {
        const int writers = 8;

        // Fire all writers in parallel.
        var tasks = Enumerable.Range(0, writers).Select(RecordOne).ToArray();
        await Task.WhenAll(tasks);

        using var ctx = CreateContext();
        var repo = new EfAuditRepository(ctx);
        var events = await repo.GetAllOrderedAsync();

        Assert.Equal(writers, events.Count);

        // Sequence numbers must be 1..N with no gaps and no duplicates.
        for (int i = 0; i < events.Count; i++)
        {
            Assert.Equal(i + 1, events[i].SequenceNumber);
        }

        // Chain integrity: each event's PreviousHash equals the previous event's Hash.
        var verifier = new AuditLedgerVerifier(repo);
        var result = await verifier.VerifyAsync();
        Assert.True(result.IsValid, $"Chain invalid: {result.FailureReason}");
        Assert.Equal(writers, result.VerifiedEventCount);
    }

    [Fact]
    public async Task ConcurrentAppends_DoNotProduceDuplicateSequenceNumbers()
    {
        const int writers = 12;

        var tasks = Enumerable.Range(0, writers).Select(RecordOne).ToArray();
        await Task.WhenAll(tasks);

        using var ctx = CreateContext();
        var repo = new EfAuditRepository(ctx);
        var events = await repo.GetAllOrderedAsync();

        var seqSet = events.Select(e => e.SequenceNumber).ToHashSet();
        Assert.Equal(writers, seqSet.Count);
    }

    [Fact]
    public async Task ConcurrentAppends_DoNotProduceDuplicateHashes()
    {
        const int writers = 10;

        var tasks = Enumerable.Range(0, writers).Select(RecordOne).ToArray();
        await Task.WhenAll(tasks);

        using var ctx = CreateContext();
        var repo = new EfAuditRepository(ctx);
        var events = await repo.GetAllOrderedAsync();

        var hashSet = events.Select(e => e.Hash).ToHashSet();
        Assert.Equal(writers, hashSet.Count);
    }
}
