using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>SQLite integration tests for <see cref="EfEvaluationResultRepository"/>.</summary>
public class EvaluationResultRepositorySqliteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfEvaluationResultRepository _repository;

    public EvaluationResultRepositorySqliteTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RebelgentDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new EfEvaluationResultRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static readonly EvaluationCase PassCase = new("case-1", "expected", "actual", true, "notes");
    private static readonly EvaluationCase FailCase = new("case-2", "expected", "actual", false, "notes");

    [Fact]
    public async Task AddAsync_PersistsResult_UsingSQLite()
    {
        var result = new EvaluationResult(Guid.NewGuid(), "dataset-1", [PassCase, FailCase], "1/2 passed.");

        await _repository.AddAsync(result);

        var count = await _db.EvaluationResults.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetByProposalIdAsync_RetrievesPersistedResult_UsingSQLite()
    {
        var proposalId = Guid.NewGuid();
        var result = new EvaluationResult(proposalId, "dataset-1", [PassCase, FailCase], "1/2 passed.");
        await _repository.AddAsync(result);

        var retrieved = await _repository.GetByProposalIdAsync(proposalId);

        var single = Assert.Single(retrieved);
        Assert.Equal(2, single.TotalCases);
        Assert.Equal(1, single.PassedCases);
        Assert.Equal(0.5, single.PassRate, precision: 6);
    }

    [Fact]
    public async Task GetByProposalIdAsync_CasesRoundTripThroughSQLite()
    {
        var proposalId = Guid.NewGuid();
        var result = new EvaluationResult(proposalId, "dataset-1", [PassCase, FailCase], "notes");
        await _repository.AddAsync(result);

        var retrieved = await _repository.GetByProposalIdAsync(proposalId);

        var cases = retrieved.Single().Cases;
        Assert.Equal(2, cases.Count);
        Assert.Contains(cases, c => c.Name == "case-1" && c.Passed);
        Assert.Contains(cases, c => c.Name == "case-2" && !c.Passed);
    }

    [Fact]
    public async Task GetByProposalIdAsync_UnknownProposal_ReturnsEmpty_UsingSQLite()
    {
        var retrieved = await _repository.GetByProposalIdAsync(Guid.NewGuid());

        Assert.Empty(retrieved);
    }

    [Fact]
    public async Task GetByProposalIdAsync_MultipleEvaluationsForSameProposal_ReturnsAll_UsingSQLite()
    {
        var proposalId = Guid.NewGuid();
        await _repository.AddAsync(new EvaluationResult(proposalId, "dataset-1", [PassCase], "first"));
        await Task.Delay(5);
        await _repository.AddAsync(new EvaluationResult(proposalId, "dataset-2", [FailCase], "second"));

        var retrieved = await _repository.GetByProposalIdAsync(proposalId);

        Assert.Equal(2, retrieved.Count);
    }
}
