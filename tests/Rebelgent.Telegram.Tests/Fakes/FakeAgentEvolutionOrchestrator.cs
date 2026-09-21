using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;

namespace Rebelgent.Telegram.Tests.Fakes;

internal class FakeAgentEvolutionOrchestrator : IAgentEvolutionOrchestrator
{
    public AgentEvolutionAnalyzeResult AnalyzeResult { get; set; } = new(
        Detected: 0,
        Created: 0,
        Duplicates: 0,
        ManagerFailures: 0,
        Summary: "Evolution analysis complete. No proposals generated.");

    public List<(Guid ProposalId, HumanPrincipal Human)> ApproveCalls { get; } = [];
    public List<(Guid ProposalId, HumanPrincipal Human)> RejectCalls { get; } = [];

    public Func<Guid, HumanPrincipal, AgentEvolutionProposal>? ApproveHandler { get; set; }
    public Func<Guid, HumanPrincipal, AgentEvolutionProposal>? RejectHandler { get; set; }

    private int _analyzeCalls;
    public int AnalyzeCalls => _analyzeCalls;

    public Task<AgentEvolutionAnalyzeResult> AnalyzeAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _analyzeCalls);
        return Task.FromResult(AnalyzeResult);
    }

    public Task<AgentEvolutionProposal> ApproveAsync(Guid proposalId, HumanPrincipal human, CancellationToken ct = default)
    {
        ApproveCalls.Add((proposalId, human));
        var handler = ApproveHandler ?? ((_, _) => throw new InvalidOperationException("ApproveHandler not configured."));
        return Task.FromResult(handler(proposalId, human));
    }

    public Task<AgentEvolutionProposal> RejectAsync(Guid proposalId, HumanPrincipal human, CancellationToken ct = default)
    {
        RejectCalls.Add((proposalId, human));
        var handler = RejectHandler ?? ((_, _) => throw new InvalidOperationException("RejectHandler not configured."));
        return Task.FromResult(handler(proposalId, human));
    }
}
