using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;
using Rebelgent.Persistence;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

public class AgentTaskRepositoryTests : IDisposable
{
    private readonly RebelgentDbContext _db;
    private readonly AgentTaskRepository _repository;

    public AgentTaskRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new RebelgentDbContext(options);
        _repository = new AgentTaskRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task AddAsync_PersistsTask()
    {
        var task = new AgentTask("project-1", "Implement login", "Add OAuth2 login", AgentRole.BackendDeveloper);

        await _repository.AddAsync(task);

        var count = await _db.AgentTasks.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsPersistedTask()
    {
        var task = new AgentTask("project-1", "Write tests", "Add unit tests for auth", AgentRole.QaEngineer);
        await _repository.AddAsync(task);

        var retrieved = await _repository.GetByIdAsync(task.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(task.Id, retrieved.Id);
        Assert.Equal(task.Title, retrieved.Title);
        Assert.Equal(task.ProjectId, retrieved.ProjectId);
        Assert.Equal(task.Description, retrieved.Description);
        Assert.Equal(task.AssignedRole, retrieved.AssignedRole);
        Assert.Equal(task.Status, retrieved.Status);
        Assert.Equal(task.Risk, retrieved.Risk);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsInDescendingCreatedAtOrder()
    {
        // Add three tasks with slight delays to ensure ordering
        var t1 = new AgentTask("proj", "Task 1", "desc", AgentRole.BackendDeveloper);
        await _repository.AddAsync(t1);

        await Task.Delay(5);
        var t2 = new AgentTask("proj", "Task 2", "desc", AgentRole.BackendDeveloper);
        await _repository.AddAsync(t2);

        await Task.Delay(5);
        var t3 = new AgentTask("proj", "Task 3", "desc", AgentRole.BackendDeveloper);
        await _repository.AddAsync(t3);

        var recent = await _repository.GetRecentAsync(3);

        Assert.Equal(3, recent.Count);
        Assert.Equal(t3.Id, recent.ElementAt(0).Id);
        Assert.Equal(t2.Id, recent.ElementAt(1).Id);
        Assert.Equal(t1.Id, recent.ElementAt(2).Id);
    }

    [Fact]
    public async Task GetRecentAsync_RespectsCountLimit()
    {
        for (var i = 0; i < 5; i++)
            await _repository.AddAsync(new AgentTask("proj", $"Task {i}", "desc", AgentRole.BackendDeveloper));

        var recent = await _repository.GetRecentAsync(3);

        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public async Task GetByIdAsync_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _repository.GetByIdAsync(Guid.NewGuid(), cts.Token));
    }
}

public class TaskServiceTests : IDisposable
{
    private readonly RebelgentDbContext _db;
    private readonly AgentTaskRepository _repository;
    private readonly TaskService _service;

    public TaskServiceTests()
    {
        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new RebelgentDbContext(options);
        _repository = new AgentTaskRepository(_db);

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskService>.Instance;
        _service = new TaskService(_repository, logger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateTaskAsync_CreatesAndPersistsTask()
    {
        var task = await _service.CreateTaskAsync(
            new CreateTaskInput("proj-1", "Build dashboard", "Create analytics dashboard"));

        Assert.NotEqual(Guid.Empty, task.Id);
        var stored = await _repository.GetByIdAsync(task.Id);
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task CreateTaskAsync_DefaultsToEngineeringManagerRole()
    {
        var task = await _service.CreateTaskAsync(
            new CreateTaskInput("proj-1", "Some task", "Some description"));

        Assert.Equal(AgentRole.EngineeringManager, task.AssignedRole);
    }

    [Fact]
    public async Task CreateTaskAsync_DefaultsToMediumRisk()
    {
        var task = await _service.CreateTaskAsync(
            new CreateTaskInput("proj-1", "Some task", "Some description"));

        Assert.Equal(RiskLevel.Medium, task.Risk);
    }

    [Fact]
    public async Task CreateTaskAsync_DefaultsToCreatedStatus()
    {
        var task = await _service.CreateTaskAsync(
            new CreateTaskInput("proj-1", "Some task", "Some description"));

        Assert.Equal(AgentTaskStatus.Created, task.Status);
    }

    [Fact]
    public async Task CreateTaskAsync_EmptyProjectId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateTaskAsync(new CreateTaskInput("", "title", "desc")));
    }

    [Fact]
    public async Task CreateTaskAsync_EmptyTitle_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateTaskAsync(new CreateTaskInput("proj", "", "desc")));
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsPersistedTask()
    {
        var created = await _service.CreateTaskAsync(
            new CreateTaskInput("proj", "My task", "desc"));

        var retrieved = await _service.GetTaskAsync(created.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(created.Id, retrieved.Id);
    }

    [Fact]
    public async Task GetRecentTasksAsync_ReturnsCreatedTask()
    {
        await _service.CreateTaskAsync(new CreateTaskInput("proj", "Task A", "desc"));

        var tasks = await _service.GetRecentTasksAsync(10);

        Assert.Single(tasks);
    }
}
