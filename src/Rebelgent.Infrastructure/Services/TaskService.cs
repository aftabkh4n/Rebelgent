using Microsoft.Extensions.Logging;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;

namespace Rebelgent.Infrastructure.Services;

/// <summary>
/// Creates and queries agent tasks.
/// Telegram handlers and API controllers must go through this service
/// rather than constructing or persisting AgentTask objects directly.
/// </summary>
public class TaskService : ITaskService
{
    private readonly IAgentTaskRepository _repository;
    private readonly TaskLifecycleService _lifecycle;
    private readonly ILogger<TaskService> _logger;

    public TaskService(IAgentTaskRepository repository, TaskLifecycleService lifecycle, ILogger<TaskService> logger)
    {
        _repository = repository;
        _lifecycle = lifecycle;
        _logger = logger;
    }

    public async Task<AgentTask> CreateTaskAsync(CreateTaskInput input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.ProjectId))
            throw new ArgumentException("ProjectId cannot be empty.", nameof(input));
        if (string.IsNullOrWhiteSpace(input.Title))
            throw new ArgumentException("Title cannot be empty.", nameof(input));
        if (input.Description is null)
            throw new ArgumentException("Description cannot be null.", nameof(input));

        var task = new AgentTask(
            input.ProjectId,
            input.Title,
            input.Description,
            AgentRole.EngineeringManager,
            RiskLevel.Medium);

        await _repository.AddAsync(task, cancellationToken);

        _logger.LogInformation("Task created: {TaskId} — {Title}", task.Id, task.Title);

        return task;
    }

    public Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int count = 20, CancellationToken cancellationToken = default)
        => _repository.GetRecentAsync(count, cancellationToken);

    public Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string prefix, int maxResults, CancellationToken cancellationToken = default)
        => _repository.FindByPrefixAsync(prefix, maxResults, cancellationToken);

    public async Task<AgentTask?> TransitionAsync(Guid id, AgentTaskStatus newStatus, CancellationToken cancellationToken = default)
    {
        var task = await _repository.GetByIdAsync(id, cancellationToken);
        if (task is null) return null;

        _lifecycle.Transition(task, newStatus);
        await _repository.UpdateAsync(task, cancellationToken);

        _logger.LogInformation("Task {TaskId} transitioned to {Status}", id, newStatus);
        return task;
    }

    public async Task<AgentTask?> RetryFailedTaskAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var task = await _repository.GetByIdAsync(id, cancellationToken);
        if (task is null) return null;

        _lifecycle.RetryFailedTask(task);
        await _repository.UpdateAsync(task, cancellationToken);
        return task;
    }

    public async Task<AgentTask?> SetBranchNameAsync(Guid id, string branchName, CancellationToken cancellationToken = default)
    {
        var task = await _repository.GetByIdAsync(id, cancellationToken);
        if (task is null) return null;

        task.SetBranchName(branchName);
        await _repository.UpdateAsync(task, cancellationToken);

        return task;
    }

    public async Task<AgentTask?> SetPullRequestInfoAsync(Guid id, int pullRequestNumber, string pullRequestUrl, CancellationToken cancellationToken = default)
    {
        var task = await _repository.GetByIdAsync(id, cancellationToken);
        if (task is null) return null;

        task.SetPullRequestInfo(pullRequestNumber, pullRequestUrl);
        await _repository.UpdateAsync(task, cancellationToken);

        _logger.LogInformation("Task {TaskId} PR #{PrNumber} created: {PrUrl}", id, pullRequestNumber, pullRequestUrl);
        return task;
    }

    public async Task<AgentTask?> SetMergeInfoAsync(Guid id, string mergeCommitSha, string mergeMethod, CancellationToken cancellationToken = default)
    {
        var task = await _repository.GetByIdAsync(id, cancellationToken);
        if (task is null) return null;

        task.SetMergeInfo(mergeCommitSha, mergeMethod);
        await _repository.UpdateAsync(task, cancellationToken);

        _logger.LogInformation("Task {TaskId} merged via {MergeMethod} at {CommitSha}", id, mergeMethod, mergeCommitSha);
        return task;
    }
}
