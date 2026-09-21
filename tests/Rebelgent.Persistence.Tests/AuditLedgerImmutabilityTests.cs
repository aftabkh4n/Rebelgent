using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Proves that the AuditEvents table is immutable at the SQLite trigger level.
/// Direct UPDATE and DELETE statements against AuditEvents must be blocked by
/// the immutability triggers created in the AddGovernance migration.
/// </summary>
public class AuditLedgerImmutabilityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfAuditRepository _repository;

    public AuditLedgerImmutabilityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RebelgentDbContext(options);
        // Must use MigrateAsync (not EnsureCreated) so that the SQLite immutability triggers
        // defined in the AddGovernance migration are actually created.
        _db.Database.Migrate();
        _repository = new EfAuditRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static AuditEvent MakeEvent(long seq, string previousHash) =>
        new(seq, DateTimeOffset.UtcNow, AuditEventType.TaskCreated, ActorType.Human, "user-1",
            "AgentTask", "task-1", "Create", "{}", previousHash);

    [Fact]
    public async Task AppendAsync_StoresEventInDatabase()
    {
        var ev = MakeEvent(1, string.Empty);
        await _repository.AppendAsync(ev);

        var count = await _db.AuditEvents.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetAllOrderedAsync_ReturnsEventsInSequenceOrder()
    {
        var first = MakeEvent(1, string.Empty);
        var second = MakeEvent(2, first.Hash);
        await _repository.AppendAsync(first);
        await _repository.AppendAsync(second);

        var events = await _repository.GetAllOrderedAsync();

        Assert.Equal(2, events.Count);
        Assert.Equal(1, events[0].SequenceNumber);
        Assert.Equal(2, events[1].SequenceNumber);
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsHighestSequenceNumber()
    {
        var first = MakeEvent(1, string.Empty);
        var second = MakeEvent(2, first.Hash);
        await _repository.AppendAsync(first);
        await _repository.AppendAsync(second);

        var latest = await _repository.GetLatestAsync();

        Assert.NotNull(latest);
        Assert.Equal(2, latest.SequenceNumber);
    }

    [Fact]
    public async Task DirectUpdate_OnAuditEvents_IsBlockedByTrigger()
    {
        var ev = MakeEvent(1, string.Empty);
        await _repository.AppendAsync(ev);

        // Attempt a direct UPDATE using raw SQL — this should be blocked by the
        // prevent_audit_events_update trigger created in AddGovernance migration.
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE AuditEvents SET ActorId = 'tampered' WHERE SequenceNumber = 1";

        var ex = await Assert.ThrowsAsync<SqliteException>(async () => await cmd.ExecuteNonQueryAsync());
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectDelete_OnAuditEvents_IsBlockedByTrigger()
    {
        var ev = MakeEvent(1, string.Empty);
        await _repository.AppendAsync(ev);

        // Attempt a direct DELETE using raw SQL — this should be blocked by the
        // prevent_audit_events_delete trigger created in AddGovernance migration.
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM AuditEvents WHERE SequenceNumber = 1";

        var ex = await Assert.ThrowsAsync<SqliteException>(async () => await cmd.ExecuteNonQueryAsync());
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsRequestedCountInDescendingOrder()
    {
        var first = MakeEvent(1, string.Empty);
        var second = MakeEvent(2, first.Hash);
        var third = MakeEvent(3, second.Hash);
        await _repository.AppendAsync(first);
        await _repository.AppendAsync(second);
        await _repository.AppendAsync(third);

        var recent = await _repository.GetRecentAsync(2);

        Assert.Equal(2, recent.Count);
        // Most recent first
        Assert.Equal(3, recent[0].SequenceNumber);
        Assert.Equal(2, recent[1].SequenceNumber);
    }
}
