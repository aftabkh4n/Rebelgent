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
    private readonly ILogger<TaskService> _logger;

    public TaskService(IAgentTaskRepository repository, ILogger<TaskService> logger)
    {
        _repository = repository;
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
}
