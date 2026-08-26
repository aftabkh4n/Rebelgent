using Rebelgent.Orchestration.Orchestrator;

namespace Rebelgent.Orchestration.Tests;

public class ExecutionConcurrencyGuardTests
{
    [Fact]
    public async Task WaitAsync_FirstAcquire_ReturnsTrue()
    {
        using var guard = new ExecutionConcurrencyGuard();
        var acquired = await guard.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(acquired);
        guard.Release();
    }

    [Fact]
    public async Task WaitAsync_SecondAcquireWhileHeld_ReturnsFalse()
    {
        using var guard = new ExecutionConcurrencyGuard();
        var first = await guard.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(first);

        var second = await guard.WaitAsync(TimeSpan.FromMilliseconds(50));
        Assert.False(second);

        guard.Release();
    }

    [Fact]
    public async Task WaitAsync_AfterRelease_ReturnsTrue()
    {
        using var guard = new ExecutionConcurrencyGuard();
        var first = await guard.WaitAsync(TimeSpan.FromSeconds(1));
        guard.Release();

        var second = await guard.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(second);
        guard.Release();
    }
}
