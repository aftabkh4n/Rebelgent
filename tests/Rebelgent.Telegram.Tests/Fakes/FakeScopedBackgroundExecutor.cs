using Microsoft.Extensions.DependencyInjection;
using Rebelgent.Core.Services;

namespace Rebelgent.Telegram.Tests.Fakes;

/// <summary>
/// Executes background work synchronously against a caller-supplied
/// <see cref="IServiceProvider"/> so tests can observe outcomes without
/// racing against a background thread. When no provider is supplied,
/// work is invoked with an empty <see cref="ServiceCollection"/> scope.
/// </summary>
internal sealed class FakeScopedBackgroundExecutor : IScopedBackgroundExecutor
{
    public List<string> Operations { get; } = [];
    public IServiceProvider? Provider { get; set; }

    public void Run(string operationName, Func<IServiceProvider, CancellationToken, Task> work)
    {
        Operations.Add(operationName);
        var provider = Provider ?? new ServiceCollection().BuildServiceProvider();
        // Fire-and-forget to match production semantics; unobserved exceptions swallowed.
        _ = Task.Run(async () =>
        {
            try { await work(provider, CancellationToken.None); }
            catch { /* swallowed for test parity */ }
        });
    }
}
