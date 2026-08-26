using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.Services;
using Rebelgent.Persistence;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// SQLite integration tests. These use a real SQLite in-memory database and expose
/// provider-specific failures that EF Core InMemory tests cannot catch.
/// </summary>
public class AgentTaskRepositorySqliteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly AgentTaskRepository _repository;

    public AgentTaskRepositorySqliteTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RebelgentDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new AgentTaskRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AddAsync_PersistsTask_UsingSQLite()
    {
        var task = new AgentTask("project-1", "Implement login", "Add OAuth2 login", AgentRole.BackendDeveloper);

        await _repository.AddAsync(task);

        var count = await _db.AgentTasks.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetByIdAsync_RetrievesPersistedTask_UsingSQLite()
    {
        var task = new AgentTask("project-1", "Write tests", "Add unit tests", AgentRole.QaEngineer);
        await _repository.AddAsync(task);

        var retrieved = await _repository.GetByIdAsync(task.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(task.Id, retrieved.Id);
        Assert.Equal(task.Title, retrieved.Title);
        Assert.Equal(task.Status, retrieved.Status);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsMultipleTasks_UsingSQLite()
    {
        var t1 = new AgentTask("proj", "Task 1", "desc", AgentRole.BackendDeveloper);
        await _repository.AddAsync(t1);

        await Task.Delay(5);
        var t2 = new AgentTask("proj", "Task 2", "desc", AgentRole.BackendDeveloper);
        await _repository.AddAsync(t2);

        var recent = await _repository.GetRecentAsync(10);

        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public async Task GetRecentAsync_OrdersNewestFirst_UsingSQLite()
    {
        var t1 = new AgentTask("proj", "Task 1", "desc", AgentRole.BackendDeveloper);
        await _repository.AddAsync(t1);

        await Task.Delay(5);
        var t2 = new AgentTask("proj", "Task 2", "desc", AgentRole.BackendDeveloper);
        await _repository.AddAsync(t2);

        await Task.Delay(5);
        var t3 = new AgentTask("proj", "Task 3", "desc", AgentRole.BackendDeveloper);
        await _repository.AddAsync(t3);

        var recent = await _repository.GetRecentAsync(3);

        Assert.Equal(t3.Id, recent.ElementAt(0).Id);
        Assert.Equal(t2.Id, recent.ElementAt(1).Id);
        Assert.Equal(t1.Id, recent.ElementAt(2).Id);
    }

    [Fact]
    public async Task GetRecentAsync_RespectsCountLimit_UsingSQLite()
    {
        for (var i = 0; i < 5; i++)
            await _repository.AddAsync(new AgentTask("proj", $"Task {i}", "desc", AgentRole.BackendDeveloper));

        var recent = await _repository.GetRecentAsync(3);

        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public async Task GetRecentAsync_WithDateTimeOffsetCreatedAt_ReturnsCorrectly_UsingSQLite()
    {
        var task = new AgentTask("proj", "DateTimeOffset test", "Tests DTO round-trip", AgentRole.QaEngineer);
        await _repository.AddAsync(task);

        var recent = await _repository.GetRecentAsync(1);

        Assert.Single(recent);
        Assert.True(recent.ElementAt(0).CreatedAt > DateTimeOffset.MinValue);
        Assert.Equal(TimeSpan.Zero, recent.ElementAt(0).CreatedAt.Offset);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull_UsingSQLite()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }
}

public class TaskServiceSqliteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly TaskService _service;
    private readonly AgentTaskRepository _repository;

    public TaskServiceSqliteTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RebelgentDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new AgentTaskRepository(_db);
        _service = new TaskService(_repository, new TaskLifecycleService(), NullLogger<TaskService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetRecentTasksAsync_AfterCreate_ReturnsTask_UsingSQLite()
    {
        await _service.CreateTaskAsync(new CreateTaskInput("proj", "Test task", "desc"));

        var tasks = await _service.GetRecentTasksAsync(10);

        Assert.Single(tasks);
    }

    [Fact]
    public async Task StatusQueryPath_CountsActiveTasks_UsingSQLite()
    {
        await _service.CreateTaskAsync(new CreateTaskInput("proj", "Task A", "desc"));
        await _service.CreateTaskAsync(new CreateTaskInput("proj", "Task B", "desc"));

        var recent = await _service.GetRecentTasksAsync(100);
        var active = recent.Count(t =>
            t.Status is not (AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled));

        Assert.Equal(2, active);
    }
}
