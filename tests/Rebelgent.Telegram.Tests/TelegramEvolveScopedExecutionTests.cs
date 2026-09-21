using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
/// Proves /evolve analyze does not capture the request-scoped
/// <see cref="IAgentEvolutionOrchestrator"/> — the background executor MUST resolve it
/// from a fresh <see cref="IServiceProvider"/>.
/// </summary>
public class TelegramEvolveScopedExecutionTests
{
    private const long AuthorizedUserId = 42L;
    private const long ChatId = 1000L;

    private sealed record BuildResult(
        TelegramUpdateHandler Handler,
        FakeMessageSender Sender,
        FakeScopedBackgroundExecutor Executor,
        FakeAgentEvolutionOrchestrator RequestScopedOrchestrator,
        FakeAgentEvolutionOrchestrator BackgroundScopedOrchestrator);

    private static BuildResult Build()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        var sender = new FakeMessageSender();
        var requestScoped = new FakeAgentEvolutionOrchestrator();
        var backgroundScoped = new FakeAgentEvolutionOrchestrator
        {
            AnalyzeResult = new AgentEvolutionAnalyzeResult(1, 1, 0, 0, "Analysis done (background scope)")
        };
        var executor = new FakeScopedBackgroundExecutor
        {
            Provider = new ServiceCollection()
                .AddScoped<IAgentEvolutionOrchestrator>(_ => backgroundScoped)
                .BuildServiceProvider()
        };

        var handler = new TelegramUpdateHandler(
            new FakeTaskService(), sender, auth, new FakeProjectRegistry(null),
            new FakeTaskOrchestrator(),
            new FakeQualityOrchestrator(), new FakePullRequestOrchestrator(),
            new FakeMergeOrchestrator(), new FakeReleaseOrchestrator(),
            new FakePackageOrchestrator(), new FakeImprovementOrchestrator(),
            new FakeExecutionRepository(), new FakeAgentLifecycleService(),
            requestScoped, // handler-scoped orchestrator (DI-injected once)
            new FakeAgentEvolutionProposalRepository(), new FakeAuditRepository(),
            new FakeAuditLedgerVerifier(), new FakeSecurityAuditDeadLetterRepository(),
            new FakeAuditRecoveryService(), executor,
            new TelegramHumanPrincipalFactory(), NullLogger<TelegramUpdateHandler>.Instance);

        return new BuildResult(handler, sender, executor, requestScoped, backgroundScoped);
    }

    private static Update TextUpdate(long userId, long chatId, string text) => new()
    {
        Id = 1,
        Message = new Message
        {
            From = new User { Id = userId, FirstName = "Test", IsBot = false },
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            Text = text,
            Date = DateTime.UtcNow
        }
    };

    [Fact]
    public async Task EvolveAnalyze_UsesScopedBackgroundExecutor_NotRequestScope()
    {
        var b = Build();

        await b.Handler.HandleAsync(TextUpdate(AuthorizedUserId, ChatId, "/evolve analyze"), CancellationToken.None);

        // Give the fire-and-forget background job a moment.
        for (int i = 0; i < 40 && b.BackgroundScopedOrchestrator.AnalyzeCalls == 0; i++) await Task.Delay(20);

        Assert.Contains("/evolve analyze", b.Executor.Operations);
        Assert.Equal(0, b.RequestScopedOrchestrator.AnalyzeCalls);
        Assert.Equal(1, b.BackgroundScopedOrchestrator.AnalyzeCalls);
    }

    [Fact]
    public async Task EvolveAnalyze_AcknowledgesRequestImmediately()
    {
        var b = Build();

        await b.Handler.HandleAsync(TextUpdate(AuthorizedUserId, ChatId, "/evolve analyze"), CancellationToken.None);

        Assert.Contains(b.Sender.SentMessages, m => m.Text.Contains("Analyzing", StringComparison.OrdinalIgnoreCase));
    }
}
