using Rebelgent.Core.Domain;

namespace Rebelgent.Core.Tests.Domain;

public class ApprovalRequestTests
{
    [Fact]
    public void Constructor_ValidArguments_CreatesRequest()
    {
        var taskId = Guid.NewGuid();
        var request = new ApprovalRequest(taskId, ApprovalType.Merge, "Merge feature branch to main");

        Assert.Equal(taskId, request.TaskId);
        Assert.Equal(ApprovalType.Merge, request.Type);
        Assert.Equal("Merge feature branch to main", request.Description);
        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.False(request.IsResolved);
        Assert.Null(request.Decision);
        Assert.Null(request.ResolvedAt);
    }

    [Fact]
    public void Constructor_EmptyTaskId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ApprovalRequest(Guid.Empty, ApprovalType.Merge, "desc"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyDescription_Throws(string description)
    {
        Assert.Throws<ArgumentException>(() =>
            new ApprovalRequest(Guid.NewGuid(), ApprovalType.Merge, description));
    }

    [Fact]
    public void Resolve_Approved_SetsDecision()
    {
        var request = new ApprovalRequest(Guid.NewGuid(), ApprovalType.Deployment, "Deploy to production");
        request.Resolve(ApprovalDecision.Approved);

        Assert.True(request.IsResolved);
        Assert.Equal(ApprovalDecision.Approved, request.Decision);
        Assert.NotNull(request.ResolvedAt);
    }

    [Fact]
    public void Resolve_Rejected_SetsDecision()
    {
        var request = new ApprovalRequest(Guid.NewGuid(), ApprovalType.PackagePublish, "Publish to NuGet");
        request.Resolve(ApprovalDecision.Rejected);

        Assert.True(request.IsResolved);
        Assert.Equal(ApprovalDecision.Rejected, request.Decision);
    }

    [Fact]
    public void Resolve_AlreadyResolved_Throws()
    {
        var request = new ApprovalRequest(Guid.NewGuid(), ApprovalType.Merge, "desc");
        request.Resolve(ApprovalDecision.Approved);

        Assert.Throws<InvalidOperationException>(() => request.Resolve(ApprovalDecision.Rejected));
    }
}
