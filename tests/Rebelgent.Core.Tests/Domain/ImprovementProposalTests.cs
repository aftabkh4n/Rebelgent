using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Tests.Domain;

public class ImprovementProposalTests
{
    private static ImprovementProposal MakeProposal() => new(
        targetProjectId: "sandbox",
        title: "Developer repeatedly fails the build",
        description: "The Developer agent has failed the build 3 times for the same reason.",
        evidence: "3 build failures in the last week.",
        targetArea: "Developer Prompt",
        suggestedChange: "Add an explicit build-before-commit reminder to the Developer prompt.",
        riskLevel: RiskLevel.Low,
        evidenceFingerprint: "ABCDEF1234567890");

    [Fact]
    public void Constructor_ValidArguments_StartsAsProposed()
    {
        var proposal = MakeProposal();

        Assert.Equal(ImprovementProposalStatus.Proposed, proposal.Status);
        Assert.NotEqual(Guid.Empty, proposal.Id);
        Assert.Null(proposal.EvaluationSummary);
        Assert.Null(proposal.CreatedTaskId);
        Assert.Null(proposal.DecidedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyTitle_Throws(string title)
    {
        Assert.Throws<ArgumentException>(() => new ImprovementProposal(
            "sandbox", title, "description", "evidence", "target", "change", RiskLevel.Low, "fingerprint"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyEvidenceFingerprint_Throws(string fingerprint)
    {
        Assert.Throws<ArgumentException>(() => new ImprovementProposal(
            "sandbox", "title", "description", "evidence", "target", "change", RiskLevel.Low, fingerprint));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyTargetProjectId_Throws(string targetProjectId)
    {
        Assert.Throws<ArgumentException>(() => new ImprovementProposal(
            targetProjectId, "title", "description", "evidence", "target", "change", RiskLevel.Low, "fingerprint"));
    }

    [Fact]
    public void BeginEvaluation_FromProposed_TransitionsToEvaluating()
    {
        var proposal = MakeProposal();

        proposal.BeginEvaluation();

        Assert.Equal(ImprovementProposalStatus.Evaluating, proposal.Status);
    }

    [Fact]
    public void BeginEvaluation_NotProposed_Throws()
    {
        var proposal = MakeProposal();
        proposal.BeginEvaluation();

        Assert.Throws<InvalidOperationException>(() => proposal.BeginEvaluation());
    }

    [Fact]
    public void CompleteEvaluation_FromEvaluating_TransitionsToAwaitingApproval()
    {
        var proposal = MakeProposal();
        proposal.BeginEvaluation();

        proposal.CompleteEvaluation("4/4 regression cases passed (100%).");

        Assert.Equal(ImprovementProposalStatus.AwaitingApproval, proposal.Status);
        Assert.Equal("4/4 regression cases passed (100%).", proposal.EvaluationSummary);
    }

    [Fact]
    public void CompleteEvaluation_NotEvaluating_Throws()
    {
        var proposal = MakeProposal();

        Assert.Throws<InvalidOperationException>(() => proposal.CompleteEvaluation("summary"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CompleteEvaluation_EmptySummary_Throws(string summary)
    {
        var proposal = MakeProposal();
        proposal.BeginEvaluation();

        Assert.Throws<ArgumentException>(() => proposal.CompleteEvaluation(summary));
    }

    private static ImprovementProposal MakeAwaitingApprovalProposal()
    {
        var proposal = MakeProposal();
        proposal.BeginEvaluation();
        proposal.CompleteEvaluation("summary");
        return proposal;
    }

    [Fact]
    public void Approve_FromAwaitingApproval_SetsCreatedTaskIdAndStatus()
    {
        var proposal = MakeAwaitingApprovalProposal();
        var taskId = Guid.NewGuid();

        proposal.Approve(taskId);

        Assert.Equal(ImprovementProposalStatus.Approved, proposal.Status);
        Assert.Equal(taskId, proposal.CreatedTaskId);
        Assert.NotNull(proposal.DecidedAt);
    }

    [Fact]
    public void Approve_NotAwaitingApproval_Throws()
    {
        var proposal = MakeProposal();

        Assert.Throws<InvalidOperationException>(() => proposal.Approve(Guid.NewGuid()));
    }

    [Fact]
    public void Approve_EmptyTaskId_Throws()
    {
        var proposal = MakeAwaitingApprovalProposal();

        Assert.Throws<ArgumentException>(() => proposal.Approve(Guid.Empty));
    }

    [Fact]
    public void Reject_FromAwaitingApproval_TransitionsToRejected()
    {
        var proposal = MakeAwaitingApprovalProposal();

        proposal.Reject();

        Assert.Equal(ImprovementProposalStatus.Rejected, proposal.Status);
        Assert.NotNull(proposal.DecidedAt);
    }

    [Fact]
    public void Reject_FromProposed_TransitionsToRejected()
    {
        var proposal = MakeProposal();

        proposal.Reject();

        Assert.Equal(ImprovementProposalStatus.Rejected, proposal.Status);
    }

    [Fact]
    public void Reject_AlreadyApproved_Throws()
    {
        var proposal = MakeAwaitingApprovalProposal();
        proposal.Approve(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => proposal.Reject());
    }

    [Fact]
    public void MarkImplemented_FromApproved_TransitionsToImplemented()
    {
        var proposal = MakeAwaitingApprovalProposal();
        proposal.Approve(Guid.NewGuid());

        proposal.MarkImplemented();

        Assert.Equal(ImprovementProposalStatus.Implemented, proposal.Status);
    }

    [Fact]
    public void MarkImplemented_NotApproved_Throws()
    {
        var proposal = MakeProposal();

        Assert.Throws<InvalidOperationException>(() => proposal.MarkImplemented());
    }

    [Fact]
    public void MarkFailed_FromAnyStatus_TransitionsToFailed()
    {
        var proposal = MakeProposal();

        proposal.MarkFailed();

        Assert.Equal(ImprovementProposalStatus.Failed, proposal.Status);
    }
}
