namespace Rebelgent.Core.Services;

/// <summary>
/// Abstraction for wrapping a set of repository operations in a single serializable transaction.
/// Used for privileged operations that must be atomic: the state transition and its audit event
/// must either both persist, or neither must persist.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="operation"/> inside a single serializable database transaction.
    /// Commits on successful completion. Rolls back if the operation throws.
    /// The underlying database provider must serialize concurrent writers so that reads
    /// performed inside the transaction (e.g. GetLatest) are not overtaken by a concurrent writer.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);
}
