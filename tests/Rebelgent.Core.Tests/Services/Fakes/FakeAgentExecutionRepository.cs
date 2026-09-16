using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Core.Tests.Services.Fakes;

internal sealed class FakeAgentExecutionRepository : IAgentExecutionRepository
{
    public List<AgentExecutionRecord> Executions { get; } = [];

    public Task AddAsync(AgentExecutionRecord record, CancellationToken cancellationToken = default)
    {
        Executions.Add(record);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AgentExecutionRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<AgentExecutionRecord?> GetLatestByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Executions.Where(e => e.TaskId == taskId).OrderByDescending(e => e.StartedAt).FirstOrDefault());

    public Task<AgentExecutionRecord?> GetLatestByTaskIdAndRoleAsync(Guid taskId, AgentRole role, CancellationToken cancellationToken = default) =>
        Task.FromResult(Executions.Where(e => e.TaskId == taskId && e.Role == role).OrderByDescending(e => e.StartedAt).FirstOrDefault());

    public Task<IReadOnlyList<AgentExecutionRecord>> GetAllByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentExecutionRecord>>(Executions.Where(e => e.TaskId == taskId).ToList());
}
