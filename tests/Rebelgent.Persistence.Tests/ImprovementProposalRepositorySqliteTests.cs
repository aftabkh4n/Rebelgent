using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>SQLite integration tests for <see cref="EfImprovementProposalRepository"/>.</summary>
public class ImprovementProposalRepositorySqliteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfImprovementProposalRepository _repository;

    public ImprovementProposalRepositorySqliteTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RebelgentDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new EfImprovementProposalRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static ImprovementProposal MakeAwaitingApprovalProposal(string fingerprint = "FP-1") =>
        MakeProposal(fingerprint, ImprovementProposalStatus.AwaitingApproval);

    private static ImprovementProposal MakeProposal(string fingerprint, ImprovementProposalStatus status)
    {
        var proposal = new ImprovementProposal(
            "sandbox",
            "Developer repeatedly fails the build",
            "The Developer agent has failed the build 3 times.",
            "3 build failures.",
            "Developer Prompt",
            "Add a build reminder.",
            RiskLevel.Low,
            fingerprint);

        if (status == ImprovementProposalStatus.Proposed)
            return proposal;

        proposal.BeginEvaluation();
        if (status == ImprovementProposalStatus.Evaluating)
            return proposal;

        proposal.CompleteEvaluation("summary");
        if (status == ImprovementProposalStatus.AwaitingApproval)
            return proposal;

        if (status == ImprovementProposalStatus.Approved)
        {
            proposal.Approve(Guid.NewGuid());
            return proposal;
        }

        if (status == ImprovementProposalStatus.Rejected)
        {
            proposal.Reject();
            return proposal;
        }

        throw new NotSupportedException($"Unsupported status {status} for this test helper.");
    }

    [Fact]
    public async Task AddAsync_PersistsProposal_UsingSQLite()
    {
        await _repository.AddAsync(MakeAwaitingApprovalProposal());

        var count = await _db.ImprovementProposals.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetByIdAsync_RetrievesPersistedProposal_UsingSQLite()
    {
        var proposal = MakeAwaitingApprovalProposal();
        await _repository.AddAsync(proposal);

        var retrieved = await _repository.GetByIdAsync(proposal.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(proposal.TargetProjectId, retrieved.TargetProjectId);
        Assert.Equal(proposal.Title, retrieved.Title);
        Assert.Equal(proposal.Status, retrieved.Status);
        Assert.Equal(proposal.EvaluationSummary, retrieved.EvaluationSummary);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull_UsingSQLite()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_PersistsStatusTransition_UsingSQLite()
    {
        var proposal = MakeAwaitingApprovalProposal();
        await _repository.AddAsync(proposal);

        var taskId = Guid.NewGuid();
        proposal.Approve(taskId);
        await _repository.UpdateAsync(proposal);

        var retrieved = await _repository.GetByIdAsync(proposal.Id);
        Assert.Equal(ImprovementProposalStatus.Approved, retrieved!.Status);
        Assert.Equal(taskId, retrieved.CreatedTaskId);
        Assert.NotNull(retrieved.DecidedAt);
    }

    [Fact]
    public async Task UpdateAsync_UnknownProposal_Throws_UsingSQLite()
    {
        var proposal = MakeAwaitingApprovalProposal();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.UpdateAsync(proposal));
    }

    [Fact]
    public async Task GetActiveByFingerprintAsync_AwaitingApprovalProposal_IsReturned_UsingSQLite()
    {
        var proposal = MakeAwaitingApprovalProposal("FP-ACTIVE");
        await _repository.AddAsync(proposal);

        var active = await _repository.GetActiveByFingerprintAsync("FP-ACTIVE");

        Assert.NotNull(active);
        Assert.Equal(proposal.Id, active.Id);
    }

    [Fact]
    public async Task GetActiveByFingerprintAsync_RejectedProposal_IsExcluded_UsingSQLite()
    {
        var proposal = MakeProposal("FP-REJECTED", ImprovementProposalStatus.Rejected);
        await _repository.AddAsync(proposal);

        var active = await _repository.GetActiveByFingerprintAsync("FP-REJECTED");

        Assert.Null(active);
    }

    [Fact]
    public async Task GetActiveByFingerprintAsync_ApprovedProposal_IsIncluded_UsingSQLite()
    {
        var proposal = MakeProposal("FP-APPROVED", ImprovementProposalStatus.Approved);
        await _repository.AddAsync(proposal);

        var active = await _repository.GetActiveByFingerprintAsync("FP-APPROVED");

        Assert.NotNull(active);
    }

    [Fact]
    public async Task GetAllAsync_OrdersNewestFirst_UsingSQLite()
    {
        var p1 = MakeAwaitingApprovalProposal("FP-1");
        await _repository.AddAsync(p1);
        await Task.Delay(5);
        var p2 = MakeAwaitingApprovalProposal("FP-2");
        await _repository.AddAsync(p2);

        var all = await _repository.GetAllAsync();

        Assert.Equal(p2.Id, all.ElementAt(0).Id);
        Assert.Equal(p1.Id, all.ElementAt(1).Id);
    }
}
