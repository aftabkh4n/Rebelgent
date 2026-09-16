using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Repositories;

/// <summary>Provider-independent persistence boundary for <see cref="ExecutionFailure"/>.</summary>
public interface IExecutionFailureRepository
{
    Task AddAsync(ExecutionFailure failure, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionFailure>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionFailure>> GetByCategoryAsync(FailureCategory category, CancellationToken cancellationToken = default);

    /// <summary>Whether a failure has already been categorized and persisted for the given
    /// agent execution — used to keep re-analysis idempotent.</summary>
    Task<bool> ExistsForExecutionAsync(Guid executionId, CancellationToken cancellationToken = default);
}
