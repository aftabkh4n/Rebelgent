using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Infrastructure.Services;

namespace Rebelgent.Core.Tests.Services;

/// <summary>
/// Proves <see cref="ScopedBackgroundExecutor"/> resolves scoped services from a fresh
/// <see cref="IServiceScope"/> per job and disposes that scope after the delegate completes.
/// </summary>
public class ScopedBackgroundExecutorTests
{
    private sealed class TrackedScoped : IDisposable
    {
        public static int LiveCount;
        public bool Disposed { get; private set; }
        public Guid InstanceId { get; } = Guid.NewGuid();
        public TrackedScoped() { Interlocked.Increment(ref LiveCount); }
        public void Dispose() { Disposed = true; Interlocked.Decrement(ref LiveCount); }
    }

    private static ScopedBackgroundExecutor BuildExecutor(out IServiceProvider rootProvider)
    {
        var services = new ServiceCollection();
        services.AddScoped<TrackedScoped>();
        rootProvider = services.BuildServiceProvider();
        var factory = rootProvider.GetRequiredService<IServiceScopeFactory>();
        return new ScopedBackgroundExecutor(factory, NullLogger<ScopedBackgroundExecutor>.Instance);
    }

    [Fact]
    public async Task Run_CreatesFreshScope_ResolvesScopedServiceInsideIt()
    {
        var executor = BuildExecutor(out _);
        var tcs = new TaskCompletionSource<Guid>();

        executor.Run("test", (sp, _) =>
        {
            var scoped = sp.GetRequiredService<TrackedScoped>();
            tcs.SetResult(scoped.InstanceId);
            return Task.CompletedTask;
        });

        var id = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Run_DisposesScopeAfterDelegateCompletes()
    {
        var executor = BuildExecutor(out _);
        TrackedScoped? captured = null;
        var tcs = new TaskCompletionSource<bool>();

        executor.Run("test", async (sp, _) =>
        {
            captured = sp.GetRequiredService<TrackedScoped>();
            await Task.Yield();
            tcs.SetResult(true);
        });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The scope disposes after the awaited delegate returns; give the finally a tick.
        for (int i = 0; i < 20 && captured?.Disposed != true; i++) await Task.Delay(20);

        Assert.NotNull(captured);
        Assert.True(captured!.Disposed);
    }

    [Fact]
    public async Task Run_MultipleJobs_EachGetOwnScope()
    {
        var executor = BuildExecutor(out _);
        var ids = new List<Guid>();
        var latch = new SemaphoreSlim(0);

        for (int i = 0; i < 3; i++)
        {
            executor.Run("test", (sp, _) =>
            {
                lock (ids) ids.Add(sp.GetRequiredService<TrackedScoped>().InstanceId);
                latch.Release();
                return Task.CompletedTask;
            });
        }

        for (int i = 0; i < 3; i++) await latch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public async Task Run_DelegateThrows_ScopeStillDisposed()
    {
        var executor = BuildExecutor(out _);
        TrackedScoped? captured = null;
        var tcs = new TaskCompletionSource<bool>();

        executor.Run("test", (sp, _) =>
        {
            captured = sp.GetRequiredService<TrackedScoped>();
            tcs.SetResult(true);
            throw new InvalidOperationException("boom");
        });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 20 && captured?.Disposed != true; i++) await Task.Delay(20);
        Assert.True(captured!.Disposed);
    }
}
