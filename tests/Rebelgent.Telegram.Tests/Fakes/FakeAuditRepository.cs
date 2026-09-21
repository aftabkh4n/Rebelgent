using Rebelgent.Core.Audit;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeAuditRepository : IAuditRepository
{
    public List<AuditEvent> Events { get; set; } = [];

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
