using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// Proves the M10 fix that <see cref="EfAgentEvolutionProposalRepository.UpdateAsync"/>
/// persists <c>TargetProjectId</c> and the new implementation-evidence columns. Prior to the
/// fix, a call to <c>BackfillLegacyTargetProject</c> would mutate the in-memory proposal but
/// silently drop the write, so a fresh <see cref="RebelgentDbContext"/> saw a blank column.
/// </summary>
public class AgentEvolutionProposalPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfAgentEvolutionProposalRepository _repo;

    public AgentEvolutionProposalPersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RebelgentDbContext>().UseSqlite(_connection).Options;
        _db = new RebelgentDbContext(options);
        _db.Database.Migrate();
        _repo = new EfAgentEvolutionProposalRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private RebelgentDbContext OpenFresh()
    {
        var options = new DbContextOptionsBuilder<RebelgentDbContext>().UseSqlite(_connection).Options;
        return new RebelgentDbContext(options);
    }

    [Fact]
    public async Task UpdateAsync_PersistsTargetProjectId_AcrossFreshDbContext()
    {
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(), AgentEvolutionProposalType.ModifyAgent,
            targetProjectId: "",
            targetAgentId: null, proposedAgentName: null, proposedRole: null,
            purpose: "P", evidence: "E", suggestedChange: "S",
            suggestedPrompt: null, suggestedCapabilities: null,
            riskLevel: RiskLevel.Low, evaluationSummary: null,
            status: AgentEvolutionProposalStatus.Approved,
            createdAt: DateTimeOffset.UtcNow, approvedAt: DateTimeOffset.UtcNow,
            createdTaskId: Guid.NewGuid());
        await _repo.AddAsync(proposal);

        proposal.RepairPersistedTargetProject("rebelgent");
        await _repo.UpdateAsync(proposal);

        using var fresh = OpenFresh();
        var freshRepo = new EfAgentEvolutionProposalRepository(fresh);
        var reloaded = await freshRepo.GetByIdAsync(proposal.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("rebelgent", reloaded!.TargetProjectId);
    }

    [Fact]
    public async Task UpdateAsync_PersistsImplementationEvidence_AcrossFreshDbContext()
    {
        var taskId = Guid.NewGuid();
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(), AgentEvolutionProposalType.ModifyAgent,
            "rebelgent", null, null, null,
            "P", "E", "S", null, null,
            RiskLevel.Low, null,
            AgentEvolutionProposalStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, taskId);
        await _repo.AddAsync(proposal);

        var mergeSha = "ade17ae7a0d06f113a7f62614eb14ff1ed440ef5";
        var mergedAt = new DateTimeOffset(2026, 9, 23, 12, 43, 48, TimeSpan.Zero);
        proposal.MarkImplemented(mergeSha, pullRequestNumber: 11, implementedAt: mergedAt);
        await _repo.UpdateAsync(proposal);

        using var fresh = OpenFresh();
        var freshRepo = new EfAgentEvolutionProposalRepository(fresh);
        var reloaded = await freshRepo.GetByIdAsync(proposal.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(AgentEvolutionProposalStatus.Implemented, reloaded!.Status);
        Assert.Equal(mergeSha, reloaded.ImplementationMergeCommitSha);
        Assert.Equal(11, reloaded.ImplementationPullRequestNumber);
        Assert.Equal(mergedAt, reloaded.ImplementedAt);
    }

    [Fact]
    public void RepairPersistedTargetProject_RejectsWhenTargetAlreadySet()
    {
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(), AgentEvolutionProposalType.ModifyAgent,
            "existing", null, null, null,
            "P", "E", "S", null, null,
            RiskLevel.Low, null,
            AgentEvolutionProposalStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => proposal.RepairPersistedTargetProject("something"));
    }

    [Fact]
    public void MarkImplemented_RejectsProposalNotInApprovedOrImplementing()
    {
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(), AgentEvolutionProposalType.ModifyAgent,
            "x", null, null, null,
            "P", "E", "S", null, null,
            RiskLevel.Low, null,
            AgentEvolutionProposalStatus.Rejected,
            DateTimeOffset.UtcNow, null, null);

        Assert.Throws<InvalidOperationException>(() =>
            proposal.MarkImplemented("sha", 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarkImplemented_RejectsEmptyMergeCommitSha()
    {
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(), AgentEvolutionProposalType.ModifyAgent,
            "x", null, null, null,
            "P", "E", "S", null, null,
            RiskLevel.Low, null,
            AgentEvolutionProposalStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Throws<ArgumentException>(() =>
            proposal.MarkImplemented("", 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void EvolutionManagerAndImprovementAnalyst_AreDistinctEnumValues()
    {
        Assert.NotEqual(AgentRole.EvolutionManager, AgentRole.ImprovementAnalyst);
        Assert.Equal(14, (int)AgentRole.ImprovementAnalyst);
        Assert.Equal(15, (int)AgentRole.EvolutionManager);
    }
}
