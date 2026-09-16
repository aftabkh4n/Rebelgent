using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;

namespace Rebelgent.Core.Tests.Services.Fakes;

internal sealed class FakeExecutionFailureRepository : IExecutionFailureRepository
{
    public List<ExecutionFailure> Failures { get; } = [];

    public Task AddAsync(ExecutionFailure failure, CancellationToken cancellationToken = default)
    {
        Failures.Add(failure);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutionFailure>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExecutionFailure>>(Failures);

    public Task<IReadOnlyList<ExecutionFailure>> GetByCategoryAsync(FailureCategory category, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExecutionFailure>>(Failures.Where(f => f.Category == category).ToList());

    public Task<bool> ExistsForExecutionAsync(Guid executionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Failures.Any(f => f.ExecutionId == executionId));
}
