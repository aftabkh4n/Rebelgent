using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

/// <summary>
/// Proves the /evolution and /agent read-only display paths surface persisted lifecycle
/// evidence (TargetProjectId, PR#, merge SHA, implemented timestamp) and that /agent's
/// details view cannot trigger any lifecycle mutation.
/// </summary>
public class TelegramEvolutionAndAgentDisplayTests
{
    private const long AuthorizedUserId = 42L;
    private const long ChatId = 1000L;

    private sealed class Wired
    {
        public required TelegramUpdateHandler Handler { get; init; }
        public required FakeMessageSender Sender { get; init; }
        public required FakeAgentEvolutionProposalRepository Proposals { get; init; }
        public required FakeAgentLifecycleService Agents { get; init; }
    }

    private static Wired Build()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        var proposals = new FakeAgentEvolutionProposalRepository();
        var agents = new FakeAgentLifecycleService();
        var sender = new FakeMessageSender();
        var handler = new TelegramUpdateHandler(
            new FakeTaskService(), sender, auth, new FakeProjectRegistry(null),
            new FakeTaskOrchestrator(), new FakeQualityOrchestrator(),
            new FakePullRequestOrchestrator(), new FakeMergeOrchestrator(),
            new FakeReleaseOrchestrator(), new FakePackageOrchestrator(),
            new FakeImprovementOrchestrator(), new FakeExecutionRepository(),
            agents, new FakeAgentEvolutionOrchestrator(), proposals,
            new FakeAuditRepository(), new FakeAuditLedgerVerifier(),
            new FakeSecurityAuditDeadLetterRepository(), new FakeAuditRecoveryService(),
            new FakeScopedBackgroundExecutor(), new TelegramHumanPrincipalFactory(),
            NullLogger<TelegramUpdateHandler>.Instance);
        return new Wired { Handler = handler, Sender = sender, Proposals = proposals, Agents = agents };
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

    [Fact]
    public async Task EvolutionCommand_ShowsPersistedTargetProjectId()
    {
        var w = Build();
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(), AgentEvolutionProposalType.ModifyAgent,
            "rebelgent", null, null, null,
            "Purpose", "Evidence", "Change", null, null,
            RiskLevel.Medium, "eval",
            AgentEvolutionProposalStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid());
        w.Proposals.Proposals.Add(proposal);

        var prefix = proposal.Id.ToString("N")[..8];
        await w.Handler.HandleAsync(TextUpdate($"/evolution {prefix}"), CancellationToken.None);

        Assert.NotEmpty(w.Sender.SentMessages);
        Assert.Contains("Target project: rebelgent", w.Sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task EvolutionCommand_Implemented_ShowsPrAndMergeAndTimestamp()
    {
        var w = Build();
        var implementedAt = new DateTimeOffset(2026, 9, 23, 12, 43, 48, TimeSpan.Zero);
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(), AgentEvolutionProposalType.ModifyAgent,
            "rebelgent", null, null, null,
            "Purpose", "Evidence", "Change", null, null,
            RiskLevel.Medium, "eval",
            AgentEvolutionProposalStatus.Implemented,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid(),
            implementationMergeCommitSha: "ade17ae7a0d06f113a7f62614eb14ff1ed440ef5",
            implementationPullRequestNumber: 11,
            implementedAt: implementedAt);
        w.Proposals.Proposals.Add(proposal);

        var prefix = proposal.Id.ToString("N")[..8];
        await w.Handler.HandleAsync(TextUpdate($"/evolution {prefix}"), CancellationToken.None);

        var text = w.Sender.SentMessages[0].Text;
        Assert.Contains("Status: Implemented", text);
        Assert.Contains("Implementation PR: #11", text);
        Assert.Contains("Merge commit: ade17ae7", text);
        Assert.Contains("Implemented: 2026-09-23", text);
    }

    [Fact]
    public async Task EvolutionCommand_BlankTarget_ShowsExplicitUnsetMarker()
    {
        var w = Build();
        var proposal = AgentEvolutionProposal.Reconstitute(
            Guid.NewGuid(), AgentEvolutionProposalType.ModifyAgent,
            "", null, null, null,
            "Purpose", "Evidence", "Change", null, null,
            RiskLevel.Medium, null,
            AgentEvolutionProposalStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid());
        w.Proposals.Proposals.Add(proposal);

        var prefix = proposal.Id.ToString("N")[..8];
        await w.Handler.HandleAsync(TextUpdate($"/evolution {prefix}"), CancellationToken.None);

        Assert.Contains("Target project: (unset)", w.Sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task AgentCommand_NoAction_ShowsReadOnlyDetails()
    {
        var w = Build();
        var agent = new AgentDefinition("Agent Evolution Manager", AgentRole.EvolutionManager,
            "Analyses per-agent performance", "Read-only Claude Code invocation", Guid.NewGuid());
        w.Agents.Agents.Add(agent);

        var prefix = agent.Id.ToString("N")[..8];
        await w.Handler.HandleAsync(TextUpdate($"/agent {prefix}"), CancellationToken.None);

        var text = w.Sender.SentMessages[0].Text;
        Assert.Contains("Agent Evolution Manager", text);
        Assert.Contains("Role: EvolutionManager", text);
        Assert.Contains("Status:", text);
    }

    [Fact]
    public async Task AgentCommand_NoAction_DoesNotMutateLifecycle()
    {
        var w = Build();
        var agent = new AgentDefinition("Existing", AgentRole.BackendDeveloper,
            "purpose", "description", Guid.NewGuid());
        w.Agents.Agents.Add(agent);
        var originalStatus = agent.Status;

        var prefix = agent.Id.ToString("N")[..8];
        await w.Handler.HandleAsync(TextUpdate($"/agent {prefix}"), CancellationToken.None);

        Assert.Equal(originalStatus, w.Agents.Agents.Single().Status);
    }

    [Fact]
    public async Task AgentCommand_UnknownAction_StillRefuses()
    {
        var w = Build();
        var agent = new AgentDefinition("Existing", AgentRole.BackendDeveloper,
            "purpose", "description", Guid.NewGuid());
        w.Agents.Agents.Add(agent);

        var prefix = agent.Id.ToString("N")[..8];
        await w.Handler.HandleAsync(TextUpdate($"/agent {prefix} explode"), CancellationToken.None);

        Assert.Contains("activate, suspend, or retire", w.Sender.SentMessages[0].Text);
    }
}
