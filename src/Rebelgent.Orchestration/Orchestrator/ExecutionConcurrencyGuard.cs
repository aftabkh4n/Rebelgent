namespace Rebelgent.Orchestration.Orchestrator;

/// <summary>
/// Singleton semaphore that limits concurrent agent executions to one.
/// Injected as a singleton so the same instance is shared across all callers.
/// </summary>
public sealed class ExecutionConcurrencyGuard : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => _semaphore.WaitAsync(timeout, cancellationToken);

    public void Release() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}
