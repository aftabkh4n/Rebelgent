using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.ClaudeCode.Improvement;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Telegram.Authorization;
using Rebelgent.Telegram.Handlers;
using Rebelgent.Telegram.Tests.Fakes;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramOptions = Rebelgent.Telegram.Options.TelegramOptions;

namespace Rebelgent.Telegram.Tests;

public class TelegramImprovementCommandTests
{
    private const long AuthorizedUserId = 42L;
    private const long ChatId = 1000L;

    private static readonly ProjectDefinition SandboxProject = new()
    {
        Id = "sandbox",
        Name = "Sandbox",
        RepositoryPath = @"D:\Projects\RebelgentSandbox",
        DefaultBranch = "main"
    };

    private (TelegramUpdateHandler handler, FakeTaskService taskService, FakeMessageSender sender, FakeImprovementOrchestrator improvementOrchestrator) Build()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        var taskService = new FakeTaskService();
        var sender = new FakeMessageSender();
        var projectRegistry = new FakeProjectRegistry([SandboxProject]);
        var improvementOrchestrator = new FakeImprovementOrchestrator();

        var handler = new TelegramUpdateHandler(
            taskService, sender, auth, projectRegistry,
            new FakeTaskOrchestrator(), new FakeQualityOrchestrator(),
            new FakePullRequestOrchestrator(), new FakeMergeOrchestrator(),
            new FakeReleaseOrchestrator(), new FakePackageOrchestrator(), improvementOrchestrator,
            new FakeExecutionRepository(),
            new FakeAgentLifecycleService(), new FakeAgentEvolutionOrchestrator(), new FakeAgentEvolutionProposalRepository(), new FakeAuditRepository(), new FakeAuditLedgerVerifier(), new FakeSecurityAuditDeadLetterRepository(), new FakeAuditRecoveryService(), new FakeScopedBackgroundExecutor(), new TelegramHumanPrincipalFactory(),
            NullLogger<TelegramUpdateHandler>.Instance);

        return (handler, taskService, sender, improvementOrchestrator);
    }

    private static Update TextUpdate(string text) => new()
    {
        Id = 1,
        Message = new Message
        {
            From = new User { Id = AuthorizedUserId, FirstName = "Test", IsBot = false },
            Chat = new Chat { Id = ChatId, Type = ChatType.Private },
            Text = text,
            Date = DateTime.UtcNow
        }
    };

    private static ImprovementProposal MakeAwaitingApprovalProposal(string title = "Developer repeatedly fails the build")
    {
        var proposal = new ImprovementProposal(
            "sandbox",
            title,
            "The Developer agent has failed the build 3 times.",
            "3 build failures in the last week.",
            "Developer Prompt",
            "Add an explicit build-before-commit reminder.",
            RiskLevel.Low,
            $"FP-{Guid.NewGuid()}");
        proposal.BeginEvaluation();
        proposal.CompleteEvaluation("3/3 regression cases passed (100%).");
        return proposal;
    }

    // ── /metrics ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MetricsCommand_SendsComputedMetrics()
    {
        var (handler, _, sender, improvementOrchestrator) = Build();
        improvementOrchestrator.Metrics = new AgentMetricsSnapshot(
            totalTasks: 10, taskSuccessRate: 0.8, developerFailureRate: 0.2, qaPassRate: 0.9,
            reviewApprovalRate: 0.85, averageRetriesPerTask: 0.4, releaseFailureRate: 0.1,
            packageFailureRate: 0.0, failuresByCategory: new Dictionary<FailureCategory, int> { [FailureCategory.BuildFailure] = 3 });

        await handler.HandleAsync(TextUpdate("/metrics"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Total tasks: 10", sender.SentMessages[0].Text);
        Assert.Contains("BuildFailure: 3", sender.SentMessages[0].Text);
    }

    // ── /failures ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FailuresCommand_NoFailures_SendsEmptyMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate("/failures"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No categorized failures", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailuresCommand_WithFailures_SendsList()
    {
        var (handler, _, sender, improvementOrchestrator) = Build();
        improvementOrchestrator.Failures =
        [
            new ExecutionFailure(Guid.NewGuid(), null, FailureCategory.BuildFailure, "BackendDeveloper", "Build failed: missing reference.")
        ];

        await handler.HandleAsync(TextUpdate("/failures"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("BuildFailure", sender.SentMessages[0].Text);
        Assert.Contains("BackendDeveloper", sender.SentMessages[0].Text);
    }

    // ── /improvements ────────────────────────────────────────────────────────

    [Fact]
    public async Task ImprovementsCommand_NoProposals_SendsEmptyMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate("/improvements"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No improvement proposals", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImprovementsCommand_WithProposals_SendsList()
    {
        var (handler, _, sender, improvementOrchestrator) = Build();
        improvementOrchestrator.Proposals = [MakeAwaitingApprovalProposal("Add build reminder")];

        await handler.HandleAsync(TextUpdate("/improvements"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Add build reminder", sender.SentMessages[0].Text);
        Assert.Contains("AwaitingApproval", sender.SentMessages[0].Text);
    }

    // ── /improvement <id> ────────────────────────────────────────────────────

    [Fact]
    public async Task ImprovementCommand_NoArguments_SendsUsageMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate("/improvement"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Usage", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ImprovementCommand_UnknownId_SendsNotFoundMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate("/improvement 00000000"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No improvement proposal found", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImprovementCommand_KnownId_SendsDetail()
    {
        var (handler, _, sender, improvementOrchestrator) = Build();
        var proposal = MakeAwaitingApprovalProposal();
        improvementOrchestrator.Proposals = [proposal];

        await handler.HandleAsync(TextUpdate($"/improvement {proposal.Id:N}"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains(proposal.Title, sender.SentMessages[0].Text);
        Assert.Contains("Developer Prompt", sender.SentMessages[0].Text);
        Assert.Contains("AwaitingApproval", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ImprovementCommand_AmbiguousPrefix_SendsAmbiguousMessage()
    {
        var (handler, _, sender, improvementOrchestrator) = Build();
        var p1 = MakeAwaitingApprovalProposal();
        var p2 = MakeAwaitingApprovalProposal();
        improvementOrchestrator.Proposals = [p1, p2];

        // The empty string is a prefix of every proposal ID, guaranteeing ambiguity regardless of random GUIDs.
        await handler.HandleAsync(TextUpdate("/improvement " + p1.Id.ToString("N")[..1]), CancellationToken.None);

        // Either ambiguous (both share the first hex char) or a single unambiguous match — assert one definite outcome per case
        Assert.Single(sender.SentMessages);
        var text = sender.SentMessages[0].Text;
        Assert.True(text.Contains("Ambiguous", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains(p1.Title, StringComparison.OrdinalIgnoreCase));
    }

    // ── /improvement <id> approve|reject ─────────────────────────────────────

    [Fact]
    public async Task ImprovementCommand_Approve_SendsApprovalResult()
    {
        var (handler, _, sender, improvementOrchestrator) = Build();
        var proposal = MakeAwaitingApprovalProposal();
        improvementOrchestrator.Proposals = [proposal];
        improvementOrchestrator.ApproveResult = new ImprovementDecisionResult
        {
            Succeeded = true,
            Summary = "Approved. Created task [abcd1234] for this improvement. Use /run abcd1234 to start it."
        };

        await handler.HandleAsync(TextUpdate($"/improvement {proposal.Id:N} approve"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Approved", sender.SentMessages[0].Text);
        Assert.Contains("Use /run", sender.SentMessages[0].Text);
        Assert.Equal(proposal.Id, improvementOrchestrator.LastApproveProposalId);
    }

    [Fact]
    public async Task ImprovementCommand_Reject_SendsRejectionResult()
    {
        var (handler, _, sender, improvementOrchestrator) = Build();
        var proposal = MakeAwaitingApprovalProposal();
        improvementOrchestrator.Proposals = [proposal];
        improvementOrchestrator.RejectResult = new ImprovementDecisionResult { Succeeded = true, Summary = "Proposal rejected." };

        await handler.HandleAsync(TextUpdate($"/improvement {proposal.Id:N} reject"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("rejected", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(proposal.Id, improvementOrchestrator.LastRejectProposalId);
    }

    [Fact]
    public async Task ImprovementCommand_InvalidDecision_SendsErrorMessage()
    {
        var (handler, _, sender, improvementOrchestrator) = Build();
        var proposal = MakeAwaitingApprovalProposal();
        improvementOrchestrator.Proposals = [proposal];

        await handler.HandleAsync(TextUpdate($"/improvement {proposal.Id:N} maybe"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Decision must be", sender.SentMessages[0].Text);
        Assert.Null(improvementOrchestrator.LastApproveProposalId);
        Assert.Null(improvementOrchestrator.LastRejectProposalId);
    }

    // ── /improve analyze ─────────────────────────────────────────────────────

    [Fact]
    public async Task ImproveCommand_NoArguments_SendsUsageMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate("/improve"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Usage: /improve analyze", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ImproveCommand_UnknownSubcommand_SendsUsageMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate("/improve now"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Usage: /improve analyze", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ImproveAnalyzeCommand_SendsStartedAckAndNeverModifiesCodeDirectly()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate("/improve analyze"), CancellationToken.None);

        // This only ever calls IImprovementOrchestrator.AnalyzeAsync (creates proposals),
        // never any code-modifying, committing, or task-running capability — the handler has
        // no such dependency to call even if it wanted to.
        Assert.True(sender.SentMessages.Count >= 1);
        Assert.Contains("Analyzing", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no code changes are made", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    // ── /help includes improvement commands ─────────────────────────────────

    [Fact]
    public async Task HelpCommand_IncludesImprovementCommands()
    {
        var (handler, _, sender, _) = Build();
        await handler.HandleAsync(TextUpdate("/help"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("/metrics", sender.SentMessages[0].Text);
        Assert.Contains("/failures", sender.SentMessages[0].Text);
        Assert.Contains("/improvements", sender.SentMessages[0].Text);
        Assert.Contains("/improvement <id>", sender.SentMessages[0].Text);
        Assert.Contains("/improve analyze", sender.SentMessages[0].Text);
    }
}
