using Rebelgent.ClaudeCode.Improvement;
using Rebelgent.Core.Domain;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeImprovementOrchestrator : IImprovementOrchestrator
{
    public ImprovementAnalyzeResult AnalyzeResult { get; set; } = new()
    {
        Succeeded = true,
        PatternsDetected = 0,
        ProposalsCreated = 0,
        DuplicatesSkipped = 0,
        Summary = "Analysis complete. No recurring failure patterns detected."
    };

    public ImprovementDecisionResult ApproveResult { get; set; } = new()
    {
        Succeeded = true,
        Summary = "Approved."
    };

    public ImprovementDecisionResult RejectResult { get; set; } = new()
    {
        Succeeded = true,
        Summary = "Rejected."
    };

    public List<ImprovementProposal> Proposals { get; set; } = [];

    public List<ExecutionFailure> Failures { get; set; } = [];

    public AgentMetricsSnapshot Metrics { get; set; } = new(
        totalTasks: 0,
        taskSuccessRate: 0,
        developerFailureRate: 0,
        qaPassRate: 0,
        reviewApprovalRate: 0,
        averageRetriesPerTask: 0,
        releaseFailureRate: 0,
        packageFailureRate: 0,
        failuresByCategory: new Dictionary<FailureCategory, int>());

    public Guid? LastApproveProposalId { get; private set; }
    public Guid? LastRejectProposalId { get; private set; }

    public Task<ImprovementAnalyzeResult> AnalyzeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(AnalyzeResult);

    public Task<ImprovementDecisionResult> ApproveAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        LastApproveProposalId = proposalId;
        return Task.FromResult(ApproveResult);
    }

    public Task<ImprovementDecisionResult> RejectAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        LastRejectProposalId = proposalId;
        return Task.FromResult(RejectResult);
    }

    public Task<ImprovementProposal?> GetProposalAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Proposals.FirstOrDefault(p => p.Id == proposalId));

    public Task<IReadOnlyList<ImprovementProposal>> GetProposalsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImprovementProposal>>(Proposals);

    public Task<IReadOnlyList<ExecutionFailure>> GetRecentFailuresAsync(int count, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExecutionFailure>>(Failures.Take(count).ToList());

    public Task<AgentMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Metrics);
}
