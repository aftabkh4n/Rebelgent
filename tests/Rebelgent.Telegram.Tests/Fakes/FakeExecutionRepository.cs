using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Telegram.Tests.Fakes;

internal sealed class FakeExecutionRepository : IAgentExecutionRepository
{
    private readonly List<AgentExecutionRecord> _records = [];

    public Task AddAsync(AgentExecutionRecord record, CancellationToken cancellationToken = default)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AgentExecutionRecord record, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<AgentExecutionRecord?> GetLatestByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var record = _records.Where(r => r.TaskId == taskId).OrderByDescending(r => r.StartedAt).FirstOrDefault();
        return Task.FromResult(record);
    }

    public Task<AgentExecutionRecord?> GetLatestByTaskIdAndRoleAsync(Guid taskId, AgentRole role, CancellationToken cancellationToken = default)
    {
        var record = _records.Where(r => r.TaskId == taskId && r.Role == role).OrderByDescending(r => r.StartedAt).FirstOrDefault();
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<AgentExecutionRecord>> GetAllByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AgentExecutionRecord> result = _records.Where(r => r.TaskId == taskId).OrderBy(r => r.StartedAt).ToList();
        return Task.FromResult(result);
    }
}
