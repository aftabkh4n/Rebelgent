using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Repositories;
using Rebelgent.Infrastructure.Services;

namespace Rebelgent.Core.Tests.Audit;

public class AuditLedgerVerifierTests
{
    private sealed class InMemoryAuditRepository : IAuditRepository
    {
        public List<AuditEvent> Events { get; } = [];

        public Task<AuditEvent> AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            Events.Add(auditEvent);
            return Task.FromResult(auditEvent);
        }

        public Task<AuditEvent?> GetLatestAsync(CancellationToken ct = default)
            => Task.FromResult(Events.Count > 0 ? Events[^1] : (AuditEvent?)null);

        public Task<IReadOnlyList<AuditEvent>> GetAllOrderedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(Events.OrderBy(e => e.SequenceNumber).ToList());

        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int count, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(Events.OrderByDescending(e => e.SequenceNumber).Take(count).ToList());
    }

    private static AuditEvent MakeEvent(long seq, string previousHash) =>
        new(seq, DateTimeOffset.UtcNow, AuditEventType.TaskCreated, ActorType.Human, "user-1",
            "AgentTask", "task-1", "Create", "{}", previousHash);

    [Fact]
    public async Task VerifyAsync_EmptyLedger_ReturnsValidWithZeroCount()
    {
        var repo = new InMemoryAuditRepository();
        var verifier = new AuditLedgerVerifier(repo);

        var result = await verifier.VerifyAsync();

        Assert.True(result.IsValid);
        Assert.Equal(0, result.VerifiedEventCount);
    }

    [Fact]
    public async Task VerifyAsync_SingleIntactEvent_ReturnsValid()
    {
        var repo = new InMemoryAuditRepository();
        var verifier = new AuditLedgerVerifier(repo);
        repo.Events.Add(MakeEvent(1, string.Empty));

        var result = await verifier.VerifyAsync();

        Assert.True(result.IsValid);
        Assert.Equal(1, result.VerifiedEventCount);
    }

    [Fact]
    public async Task VerifyAsync_MultipleIntactEvents_ReturnsValid()
    {
        var repo = new InMemoryAuditRepository();
        var verifier = new AuditLedgerVerifier(repo);

        var first = MakeEvent(1, string.Empty);
        repo.Events.Add(first);
        var second = MakeEvent(2, first.Hash);
        repo.Events.Add(second);
        var third = MakeEvent(3, second.Hash);
        repo.Events.Add(third);

        var result = await verifier.VerifyAsync();

        Assert.True(result.IsValid);
        Assert.Equal(3, result.VerifiedEventCount);
    }

    [Fact]
    public async Task VerifyAsync_SequenceNumberGap_ReturnsInvalid()
    {
        var repo = new InMemoryAuditRepository();
        var verifier = new AuditLedgerVerifier(repo);

        var first = MakeEvent(1, string.Empty);
        repo.Events.Add(first);
        // Skip sequence 2 — use 3 directly
        var third = MakeEvent(3, first.Hash);
        repo.Events.Add(third);

        var result = await verifier.VerifyAsync();

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("sequence", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_TamperedHash_ReturnsInvalid()
    {
        var repo = new InMemoryAuditRepository();
        var verifier = new AuditLedgerVerifier(repo);

        var first = MakeEvent(1, string.Empty);
        // Reconstitute with a tampered hash to simulate tampering
        var tampered = AuditEvent.Reconstitute(
            first.Id, first.SequenceNumber, first.TimestampUtc, first.EventType,
            first.ActorType, first.ActorId, first.ResourceType, first.ResourceId,
            first.Action, first.PayloadJson, first.PreviousHash,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"); // fake hash
        repo.Events.Add(tampered);

        var result = await verifier.VerifyAsync();

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_TamperedPreviousHash_ReturnsInvalid()
    {
        var repo = new InMemoryAuditRepository();
        var verifier = new AuditLedgerVerifier(repo);

        var first = MakeEvent(1, string.Empty);
        repo.Events.Add(first);
        // Second event claims a wrong previous hash
        var second = MakeEvent(2, "wrongprevioushash");
        repo.Events.Add(second);

        var result = await verifier.VerifyAsync();

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("PreviousHash", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }
}
