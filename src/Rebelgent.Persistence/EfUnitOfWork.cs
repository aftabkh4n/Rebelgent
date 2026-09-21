using System.Data;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Services;

namespace Rebelgent.Persistence;

/// <summary>
/// EF Core + SQLite implementation of <see cref="IUnitOfWork"/>.
/// Uses <see cref="IsolationLevel.Serializable"/> which the Microsoft.Data.Sqlite provider
/// maps to <c>BEGIN IMMEDIATE</c>. This acquires the RESERVED write lock at transaction
/// start, so a concurrent writer cannot interleave between <c>GetLatest</c> and <c>Append</c>
/// inside the transaction — chain sequence numbers and previous-hash references remain consistent.
/// </summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly RebelgentDbContext _db;

    public EfUnitOfWork(RebelgentDbContext db)
    {
        _db = db;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        // If a transaction is already active (e.g. tests wrapping calls), just run inline.
        if (_db.Database.CurrentTransaction is not null)
            return await operation(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await operation(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        await ExecuteInTransactionAsync<object?>(async token =>
        {
            await operation(token);
            return null;
        }, ct);
    }
}
