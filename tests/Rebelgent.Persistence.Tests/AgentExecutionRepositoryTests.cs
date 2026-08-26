using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>SQLite integration tests for AgentExecutionRepository.</summary>
public class AgentExecutionRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly IAgentExecutionRepository _repository;

    public AgentExecutionRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RebelgentDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new AgentExecutionRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AddAsync_PersistsRecord()
    {
        var record = new AgentExecutionRecord(Guid.NewGuid(), "sandbox", @"D:\ws\task1", "agent/sandbox/abc12345");
        await _repository.AddAsync(record);

        var retrieved = await _repository.GetLatestByTaskIdAsync(record.TaskId);
        Assert.NotNull(retrieved);
        Assert.Equal(record.Id, retrieved.Id);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesStatus()
    {
        var taskId = Guid.NewGuid();
        var record = new AgentExecutionRecord(taskId, "sandbox", @"D:\ws\task1", "agent/sandbox/abc12345");
        await _repository.AddAsync(record);

        record.MarkRunning();
        await _repository.UpdateAsync(record);

        var retrieved = await _repository.GetLatestByTaskIdAsync(taskId);
        Assert.NotNull(retrieved);
        Assert.Equal(ExecutionStatus.Running, retrieved.Status);
    }

    [Fact]
    public async Task GetLatestByTaskIdAsync_NoRecords_ReturnsNull()
    {
        var result = await _repository.GetLatestByTaskIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestByTaskIdAsync_MultipleRecords_ReturnsLatest()
    {
        var taskId = Guid.NewGuid();

        var first = new AgentExecutionRecord(taskId, "sandbox", @"D:\ws\first", "agent/sandbox/first");
        await _repository.AddAsync(first);

        // Small delay so StartedAt differs
        await Task.Delay(10);

        var second = new AgentExecutionRecord(taskId, "sandbox", @"D:\ws\second", "agent/sandbox/second");
        await _repository.AddAsync(second);

        var result = await _repository.GetLatestByTaskIdAsync(taskId);
        Assert.NotNull(result);
        Assert.Equal(second.Id, result.Id);
    }

    [Fact]
    public async Task UpdateAsync_AfterComplete_PersistsOutputs()
    {
        var taskId = Guid.NewGuid();
        var record = new AgentExecutionRecord(taskId, "sandbox", @"D:\ws\task", "agent/sandbox/xyz");
        await _repository.AddAsync(record);

        record.Complete("agent output", "build output", "test output", true, true);
        await _repository.UpdateAsync(record);

        var retrieved = await _repository.GetLatestByTaskIdAsync(taskId);
        Assert.NotNull(retrieved);
        Assert.Equal(ExecutionStatus.Succeeded, retrieved.Status);
        Assert.Equal("agent output", retrieved.AgentOutput);
        Assert.Equal("build output", retrieved.BuildOutput);
        Assert.Equal("test output", retrieved.TestOutput);
    }
}
