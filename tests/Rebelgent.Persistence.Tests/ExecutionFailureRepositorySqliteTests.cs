using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>SQLite integration tests for <see cref="EfExecutionFailureRepository"/>.</summary>
public class ExecutionFailureRepositorySqliteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfExecutionFailureRepository _repository;

    public ExecutionFailureRepositorySqliteTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RebelgentDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new EfExecutionFailureRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AddAsync_PersistsFailure_UsingSQLite()
    {
        var taskId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var failure = new ExecutionFailure(taskId, executionId, FailureCategory.BuildFailure, "BackendDeveloper", "Build failed.");

        await _repository.AddAsync(failure);

        var count = await _db.ExecutionFailures.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsPersistedFailures_UsingSQLite()
    {
        await _repository.AddAsync(new ExecutionFailure(Guid.NewGuid(), Guid.NewGuid(), FailureCategory.BuildFailure, "BackendDeveloper", "1"));
        await _repository.AddAsync(new ExecutionFailure(Guid.NewGuid(), Guid.NewGuid(), FailureCategory.TestFailure, "BackendDeveloper", "2"));

        var all = await _repository.GetAllAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetByCategoryAsync_FiltersByCategory_UsingSQLite()
    {
        await _repository.AddAsync(new ExecutionFailure(Guid.NewGuid(), Guid.NewGuid(), FailureCategory.BuildFailure, "BackendDeveloper", "1"));
        await _repository.AddAsync(new ExecutionFailure(Guid.NewGuid(), Guid.NewGuid(), FailureCategory.QaRejection, "QaEngineer", "2"));

        var buildFailures = await _repository.GetByCategoryAsync(FailureCategory.BuildFailure);

        Assert.Single(buildFailures);
        Assert.Equal(FailureCategory.BuildFailure, buildFailures[0].Category);
    }

    [Fact]
    public async Task ExistsForExecutionAsync_KnownExecutionId_ReturnsTrue_UsingSQLite()
    {
        var executionId = Guid.NewGuid();
        await _repository.AddAsync(new ExecutionFailure(Guid.NewGuid(), executionId, FailureCategory.BuildFailure, "BackendDeveloper", "1"));

        var exists = await _repository.ExistsForExecutionAsync(executionId);

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsForExecutionAsync_UnknownExecutionId_ReturnsFalse_UsingSQLite()
    {
        var exists = await _repository.ExistsForExecutionAsync(Guid.NewGuid());

        Assert.False(exists);
    }

    [Fact]
    public async Task GetAllAsync_RoundTripsCategoryAndTimestamp_UsingSQLite()
    {
        var failure = new ExecutionFailure(Guid.NewGuid(), Guid.NewGuid(), FailureCategory.WorkspaceFailure, "BackendDeveloper", "Workspace already exists.");
        await _repository.AddAsync(failure);

        var all = await _repository.GetAllAsync();

        var persisted = Assert.Single(all);
        Assert.Equal(FailureCategory.WorkspaceFailure, persisted.Category);
        Assert.Equal("Workspace already exists.", persisted.Message);
        Assert.Equal(TimeSpan.Zero, persisted.DetectedAt.Offset);
    }
}
