using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Proves the SecurityAuditDeadLetters and SecurityAuditDeadLetterRecoveries tables are immutable
/// at the SQLite trigger level. Direct UPDATE and DELETE are blocked by the triggers installed
/// in the AddSecurityAuditDeadLetter migration.
/// </summary>
public class SecurityAuditDeadLetterImmutabilityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfSecurityAuditDeadLetterRepository _repository;

    public SecurityAuditDeadLetterImmutabilityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RebelgentDbContext>().UseSqlite(_connection).Options;
        _db = new RebelgentDbContext(options);
        _db.Database.Migrate();
        _repository = new EfSecurityAuditDeadLetterRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static SecurityAuditDeadLetter MakeEntry() =>
        new(DateTimeOffset.UtcNow, AuditEventType.UnauthorizedApprovalAttempt, ActorType.Agent,
            "actor-1", "Security", "res-1", "Activate", "{}", "primary error");

    [Fact]
    public async Task AppendAsync_StoresEntryInDatabase()
    {
        await _repository.AppendAsync(MakeEntry());

        var count = await _db.SecurityAuditDeadLetters.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DiagnosticListTriggers()
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger'";
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        Assert.Contains(names, n => n.Contains("security_audit_dead_letters_update"));
        Assert.Contains(names, n => n.Contains("security_audit_dead_letters_delete"));
        Assert.Contains(names, n => n.Contains("security_audit_dead_letter_recoveries_update"));
        Assert.Contains(names, n => n.Contains("security_audit_dead_letter_recoveries_delete"));
    }

    [Fact]
    public async Task DirectUpdate_OnSecurityAuditDeadLetters_IsBlockedByTrigger()
    {
        var e = MakeEntry();
        await _repository.AppendAsync(e);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE SecurityAuditDeadLetters SET ActorId = 'tampered'";

        var ex = await Assert.ThrowsAsync<SqliteException>(async () => await cmd.ExecuteNonQueryAsync());
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectDelete_OnSecurityAuditDeadLetters_IsBlockedByTrigger()
    {
        var e = MakeEntry();
        await _repository.AppendAsync(e);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SecurityAuditDeadLetters";

        var ex = await Assert.ThrowsAsync<SqliteException>(async () => await cmd.ExecuteNonQueryAsync());
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectUpdate_OnSecurityAuditDeadLetterRecoveries_IsBlockedByTrigger()
    {
        var e = MakeEntry();
        await _repository.AppendAsync(e);
        var recovery = new SecurityAuditDeadLetterRecovery(e.Id, Guid.NewGuid(), Guid.NewGuid());
        await _repository.AppendRecoveryAsync(recovery);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE SecurityAuditDeadLetterRecoveries SET RecoveredAuditEventId = $x";
        cmd.Parameters.AddWithValue("$x", Guid.NewGuid().ToString());

        var ex = await Assert.ThrowsAsync<SqliteException>(async () => await cmd.ExecuteNonQueryAsync());
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectDelete_OnSecurityAuditDeadLetterRecoveries_IsBlockedByTrigger()
    {
        var e = MakeEntry();
        await _repository.AppendAsync(e);
        var recovery = new SecurityAuditDeadLetterRecovery(e.Id, Guid.NewGuid(), Guid.NewGuid());
        await _repository.AppendRecoveryAsync(recovery);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SecurityAuditDeadLetterRecoveries";

        var ex = await Assert.ThrowsAsync<SqliteException>(async () => await cmd.ExecuteNonQueryAsync());
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CountUnresolvedAsync_ExcludesRecoveredEntries()
    {
        var a = MakeEntry();
        var b = MakeEntry();
        await _repository.AppendAsync(a);
        await _repository.AppendAsync(b);

        Assert.Equal(2, await _repository.CountUnresolvedAsync());

        await _repository.AppendRecoveryAsync(new SecurityAuditDeadLetterRecovery(a.Id, Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(1, await _repository.CountUnresolvedAsync());
    }

    [Fact]
    public async Task GetUnresolvedAsync_ReturnsOnlyEntriesWithoutRecoveryLink()
    {
        var a = MakeEntry();
        var b = MakeEntry();
        await _repository.AppendAsync(a);
        await _repository.AppendAsync(b);
        await _repository.AppendRecoveryAsync(new SecurityAuditDeadLetterRecovery(a.Id, Guid.NewGuid(), Guid.NewGuid()));

        var unresolved = await _repository.GetUnresolvedAsync();
        Assert.Single(unresolved);
        Assert.Equal(b.Id, unresolved[0].Id);
    }
}
