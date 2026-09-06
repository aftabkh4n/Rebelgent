using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>SQLite integration tests for IAgentExecutionRepository role-based query methods.</summary>
public class AgentExecutionRoleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly IAgentExecutionRepository _repo;

    public AgentExecutionRoleTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new RebelgentDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new AgentExecutionRepository(_db);
    }

    private static AgentExecutionRecord MakeRecord(Guid taskId, AgentRole role = AgentRole.BackendDeveloper) =>
        new(taskId, "proj", @"C:\workspace", "branch", role, "ClaudeCode");

    [Fact]
    public async Task GetLatestByTaskIdAndRoleAsync_ReturnsMatchingRole()
    {
        var taskId = Guid.NewGuid();
        var devRecord = MakeRecord(taskId, AgentRole.BackendDeveloper);
        var qaRecord = MakeRecord(taskId, AgentRole.QaEngineer);
        await _repo.AddAsync(devRecord);
        await _repo.AddAsync(qaRecord);

        var result = await _repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.QaEngineer);

        Assert.NotNull(result);
        Assert.Equal(AgentRole.QaEngineer, result.Role);
    }

    [Fact]
    public async Task GetLatestByTaskIdAndRoleAsync_NoMatch_ReturnsNull()
    {
        var taskId = Guid.NewGuid();
        await _repo.AddAsync(MakeRecord(taskId, AgentRole.BackendDeveloper));

        var result = await _repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.QaEngineer);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllByTaskIdAsync_ReturnsAllExecutionsForTask()
    {
        var taskId = Guid.NewGuid();
        var otherTaskId = Guid.NewGuid();
        await _repo.AddAsync(MakeRecord(taskId, AgentRole.BackendDeveloper));
        await _repo.AddAsync(MakeRecord(taskId, AgentRole.QaEngineer));
        await _repo.AddAsync(MakeRecord(otherTaskId, AgentRole.BackendDeveloper));

        var results = await _repo.GetAllByTaskIdAsync(taskId);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(taskId, r.TaskId));
    }

    [Fact]
    public async Task GetAllByTaskIdAsync_EmptyResult_WhenNoRecords()
    {
        var results = await _repo.GetAllByTaskIdAsync(Guid.NewGuid());
        Assert.Empty(results);
    }

    [Fact]
    public async Task AddAsync_PersistsRoleAndProvider()
    {
        var taskId = Guid.NewGuid();
        var record = new AgentExecutionRecord(taskId, "proj", @"C:\ws", "branch", AgentRole.CodeReviewer, "ClaudeCode");
        await _repo.AddAsync(record);

        var retrieved = await _repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.CodeReviewer);

        Assert.NotNull(retrieved);
        Assert.Equal(AgentRole.CodeReviewer, retrieved.Role);
        Assert.Equal("ClaudeCode", retrieved.Provider);
    }

    [Fact]
    public async Task AddAsync_CompleteWithFindings_PersistsFindings()
    {
        var taskId = Guid.NewGuid();
        var record = new AgentExecutionRecord(taskId, "proj", @"C:\ws", "branch", AgentRole.QaEngineer, "ClaudeCode");
        await _repo.AddAsync(record);

        record.CompleteWithFindings("agent output", "QA_PASSED: all good");
        await _repo.UpdateAsync(record);

        var retrieved = await _repo.GetLatestByTaskIdAndRoleAsync(taskId, AgentRole.QaEngineer);
        Assert.NotNull(retrieved);
        Assert.Equal("QA_PASSED: all good", retrieved.Findings);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
