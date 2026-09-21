using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.Core.Authority;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Telegram.Authorization;
using Rebelgent.Telegram.Handlers;
using Rebelgent.Telegram.Tests.Fakes;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramOptions = Rebelgent.Telegram.Options.TelegramOptions;

namespace Rebelgent.Telegram.Tests;

/// <summary>
/// Tests that authority boundary enforcement is wired correctly in the Telegram update handler.
/// Covers: HumanPrincipal creation, HumanAuthorizationException handling, and unauthorized access.
/// </summary>
public class TelegramAuthorityBoundaryTests
{
    private const long AuthorizedUserId = 42L;
    private const long ChatId = 1000L;

    private sealed class AuthorizationThrowingLifecycleService : IAgentLifecycleService
    {
        private readonly List<AgentDefinition> _agents;
        private readonly HumanAuthorizationException _exception;

        public AuthorizationThrowingLifecycleService(List<AgentDefinition> agents, HumanAuthorizationException exception)
        {
            _agents = agents;
            _exception = exception;
        }

        public Task<AgentDefinition> ActivateAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default)
            => Task.FromException<AgentDefinition>(_exception);

        public Task<AgentDefinition> SuspendAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default)
            => Task.FromException<AgentDefinition>(_exception);

        public Task<AgentDefinition> RetireAsync(Guid agentId, HumanPrincipal human, CancellationToken ct = default)
            => Task.FromException<AgentDefinition>(_exception);

        public Task<AgentDefinition> GetAsync(Guid agentId, CancellationToken ct = default)
            => Task.FromResult(_agents.First(a => a.Id == agentId));

        public Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentDefinition>>(_agents);
    }

    private (TelegramUpdateHandler handler, FakeMessageSender sender) Build(
        IAgentLifecycleService? lifecycleService = null,
        FakeAgentEvolutionOrchestrator? evolutionOrchestrator = null,
        FakeAgentEvolutionProposalRepository? evolutionRepository = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        var sender = new FakeMessageSender();
        var service = lifecycleService ?? new FakeAgentLifecycleService();
        var handler = new TelegramUpdateHandler(
            new FakeTaskService(), sender, auth, new FakeProjectRegistry(),
            new FakeTaskOrchestrator(), new FakeQualityOrchestrator(),
            new FakePullRequestOrchestrator(), new FakeMergeOrchestrator(),
            new FakeReleaseOrchestrator(), new FakePackageOrchestrator(), new FakeImprovementOrchestrator(),
            new FakeExecutionRepository(),
            service, evolutionOrchestrator ?? new FakeAgentEvolutionOrchestrator(), evolutionRepository ?? new FakeAgentEvolutionProposalRepository(),
            new FakeAuditRepository(), new FakeAuditLedgerVerifier(), new FakeSecurityAuditDeadLetterRepository(), new FakeAuditRecoveryService(), new FakeScopedBackgroundExecutor(), new TelegramHumanPrincipalFactory(),
            NullLogger<TelegramUpdateHandler>.Instance);
        return (handler, sender);
    }

    private static Update TextUpdate(long userId, string text) => new()
    {
        Id = 1,
        Message = new Message
        {
            From = new User { Id = userId, FirstName = "Test", IsBot = false },
            Chat = new Chat { Id = ChatId, Type = ChatType.Private },
            Text = text,
            Date = DateTime.UtcNow
        }
    };

    [Fact]
    public void TelegramHumanPrincipalFactory_CreateFromTelegramUserId_ReturnsValidPrincipal()
    {
        var factory = new TelegramHumanPrincipalFactory();
        var principal = factory.CreateFromTelegramUserId(AuthorizedUserId);

        Assert.NotEqual(Guid.Empty, principal.HumanId);
        Assert.Equal("Telegram", principal.IdentityProvider);
        Assert.Equal(AuthorizedUserId.ToString(), principal.ExternalIdentityId);
        Assert.NotEmpty(principal.Capabilities);
    }

    [Fact]
    public void TelegramHumanPrincipalFactory_SameUserId_ReturnsSameHumanId()
    {
        var factory = new TelegramHumanPrincipalFactory();
        var first = factory.CreateFromTelegramUserId(AuthorizedUserId);
        var second = factory.CreateFromTelegramUserId(AuthorizedUserId);

        Assert.Equal(first.HumanId, second.HumanId);
    }

    [Fact]
    public async Task AgentCommand_HumanAuthorizationExceptionThrown_SendsAuthorizationDeniedMessage()
    {
        // Arrange a lifecycle service that returns an agent (for GetAllAsync) but throws
        // HumanAuthorizationException on the lifecycle action (for ActivateAsync).
        // We create the agent first so we know its Id.
        var existingAgent = new AgentDefinition(
            "TestAgent", AgentRole.BackendDeveloper,
            "Purpose", "Description", Guid.NewGuid());
        var agentId = existingAgent.Id;

        var authException = new HumanAuthorizationException(
            ActorType.Human,
            "Activate",
            agentId.ToString(),
            AuthorityViolationKind.CapabilityMissing,
            "Human principal lacks the required capability 'ActivateAgent' for action 'Activate'.");

        var throwingService = new AuthorizationThrowingLifecycleService([existingAgent], authException);
        var (handler, sender) = Build(throwingService);
        var prefix = agentId.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, $"/agent {prefix} activate"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Authorization denied", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvolutionCommand_ShowProposal_IncludesTargetProjectAndCreatedTask()
    {
        var proposal = new AgentEvolutionProposal(
            AgentEvolutionProposalType.ModifyAgent, "sandbox", "Improve role mapping",
            "Observed compatibility collision", "Introduce a distinct manager role", RiskLevel.Medium);
        proposal.BeginEvaluation();
        proposal.CompleteEvaluation("Reviewed evidence");
        proposal.Approve(Guid.NewGuid());

        var repository = new FakeAgentEvolutionProposalRepository { Proposals = [proposal] };
        var (handler, sender) = Build(evolutionRepository: repository);

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, $"/evolution {proposal.Id:N}"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Target project: sandbox", sender.SentMessages[0].Text);
        Assert.Contains("Created task:", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task EvolutionCommand_Approve_ReturnsCreatedTaskAndRunInstruction()
    {
        var proposal = new AgentEvolutionProposal(
            AgentEvolutionProposalType.ModifyAgent, "sandbox", "Improve role mapping",
            "Observed compatibility collision", "Introduce a distinct manager role", RiskLevel.Medium);
        proposal.BeginEvaluation();
        proposal.CompleteEvaluation("Reviewed evidence");

        var repository = new FakeAgentEvolutionProposalRepository { Proposals = [proposal] };
        var evolution = new FakeAgentEvolutionOrchestrator
        {
            ApproveHandler = (_, _) =>
            {
                proposal.Approve(Guid.Parse("11111111-1111-1111-1111-111111111111"));
                return proposal;
            }
        };
        var (handler, sender) = Build(evolutionOrchestrator: evolution, evolutionRepository: repository);

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, $"/evolution {proposal.Id:N} approve"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Implementation task [11111111] created", sender.SentMessages[0].Text);
        Assert.Contains("Use /run 11111111", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task EvolutionCommand_ValidationFailure_ReturnsSafeRecoveryMessage()
    {
        var proposal = new AgentEvolutionProposal(
            AgentEvolutionProposalType.ModifyAgent, "sandbox", "Improve role mapping",
            "Observed compatibility collision", "Introduce a distinct manager role", RiskLevel.Medium);
        proposal.BeginEvaluation();
        proposal.CompleteEvaluation("Reviewed evidence");

        var repository = new FakeAgentEvolutionProposalRepository { Proposals = [proposal] };
        var evolution = new FakeAgentEvolutionOrchestrator
        {
            ApproveHandler = (_, _) => throw new InvalidOperationException("AgentEvolution:ProjectId is not a registered project.")
        };
        var (handler, sender) = Build(evolutionOrchestrator: evolution, evolutionRepository: repository);

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, $"/evolution {proposal.Id:N} approve"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("recovery refused", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AgentEvolution:", sender.SentMessages[0].Text, StringComparison.Ordinal);
    }
}
