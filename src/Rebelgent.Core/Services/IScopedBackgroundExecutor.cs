namespace Rebelgent.Core.Services;

/// <summary>
/// Runs fire-and-forget background work inside a FRESH dependency-injection scope.
/// The delegate must resolve every scoped service (DbContext, repositories,
/// orchestrators) from the <see cref="IServiceProvider"/> passed in — it must NEVER
/// capture services from the caller's scope, which will be disposed when the request
/// that triggered the background work completes.
/// </summary>
public interface IScopedBackgroundExecutor
{
    void Run(string operationName, Func<IServiceProvider, CancellationToken, Task> work);
}
