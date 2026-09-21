using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebelgent.Core.Services;

namespace Rebelgent.Infrastructure.Services;

/// <summary>
/// Default <see cref="IScopedBackgroundExecutor"/> implementation. Creates a new
/// <see cref="IServiceScope"/> per background job and disposes it after the delegate
/// completes. Exceptions are logged; the background task never throws to unobserved.
/// </summary>
public sealed class ScopedBackgroundExecutor : IScopedBackgroundExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScopedBackgroundExecutor> _logger;

    public ScopedBackgroundExecutor(IServiceScopeFactory scopeFactory, ILogger<ScopedBackgroundExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Run(string operationName, Func<IServiceProvider, CancellationToken, Task> work)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            try
            {
                await work(scope.ServiceProvider, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Background operation '{Operation}' failed.", operationName);
            }
        });
    }
}
