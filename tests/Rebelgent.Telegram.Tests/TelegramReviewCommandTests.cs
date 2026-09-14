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

public class TelegramReviewCommandTests
{
    private const long AuthorizedUserId = 42L;
    private const long ChatId = 1000L;

    private static readonly ProjectDefinition SandboxProject = new()
    {
        Id = "sandbox",
        Name = "Rebelgent Sandbox",
        RepositoryPath = @"D:\Projects\RebelgentSandbox",
        DefaultBranch = "main"
    };

    private (TelegramUpdateHandler handler, FakeTaskService taskService, FakeMessageSender sender, FakeQualityOrchestrator qualityOrchestrator, FakeExecutionRepository executionRepo) Build()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        var taskService = new FakeTaskService();
        var sender = new FakeMessageSender();
        var projectRegistry = new FakeProjectRegistry([SandboxProject]);
        var orchestrator = new FakeTaskOrchestrator();
        var qualityOrchestrator = new FakeQualityOrchestrator();
        var executionRepo = new FakeExecutionRepository();
        var handler = new TelegramUpdateHandler(
            taskService, sender, auth, projectRegistry, orchestrator,
            qualityOrchestrator, new FakePullRequestOrchestrator(), new FakeMergeOrchestrator(), new FakeReleaseOrchestrator(), executionRepo,
            NullLogger<TelegramUpdateHandler>.Instance);
        return (handler, taskService, sender, qualityOrchestrator, executionRepo);
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

    // --- /review ---

    [Fact]
    public async Task ReviewCommand_NoArguments_SendsUsageMessage()
    {
        var (handler, _, sender, _, _) = Build();

        await handler.HandleAsync(TextUpdate("/review"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Usage:", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ReviewCommand_TaskIdOnly_TriggersQaReview()
    {
        var (handler, taskService, sender, _, _) = Build();
        var task = await taskService.CreateTaskAsync(new CreateTaskInput("sandbox", "Test", "desc"));
        var prefix = task.Id.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate($"/review {prefix}"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("QA", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewCommand_InvalidDecision_SendsErrorMessage()
    {
        var (handler, taskService, sender, _, _) = Build();
        var task = await taskService.CreateTaskAsync(new CreateTaskInput("sandbox", "Test", "desc"));
        var prefix = task.Id.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate($"/review {prefix} invalid"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("approve", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ReviewCommand_Approve_SendsApprovedMessage()
    {
        var (handler, taskService, sender, _, _) = Build();
        var task = await taskService.CreateTaskAsync(new CreateTaskInput("sandbox", "Test", "desc"));
        var prefix = task.Id.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate($"/review {prefix} approve"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("approved", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewCommand_Reject_SendsRejectedMessage()
    {
        var (handler, taskService, sender, _, _) = Build();
        var task = await taskService.CreateTaskAsync(new CreateTaskInput("sandbox", "Test", "desc"));
        var prefix = task.Id.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate($"/review {prefix} reject"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("rejected", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewCommand_Rerun_SendsStartedMessage()
    {
        var (handler, taskService, sender, _, _) = Build();
        var task = await taskService.CreateTaskAsync(new CreateTaskInput("sandbox", "Test", "desc"));
        var prefix = task.Id.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate($"/review {prefix} rerun"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("QA", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewCommand_NoMatchingTask_SendsNotFoundMessage()
    {
        var (handler, _, sender, _, _) = Build();

        await handler.HandleAsync(TextUpdate("/review 00000000 approve"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No task found", sender.SentMessages[0].Text);
    }

    // --- /reviews ---

    [Fact]
    public async Task ReviewsCommand_NoArguments_SendsUsageMessage()
    {
        var (handler, _, sender, _, _) = Build();

        await handler.HandleAsync(TextUpdate("/reviews"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Usage:", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ReviewsCommand_NoMatchingTask_SendsNotFoundMessage()
    {
        var (handler, _, sender, _, _) = Build();

        await handler.HandleAsync(TextUpdate("/reviews 00000000"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No task found", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ReviewsCommand_NoExecutions_SendsNoExecutionsMessage()
    {
        var (handler, taskService, sender, _, _) = Build();
        var task = await taskService.CreateTaskAsync(new CreateTaskInput("sandbox", "Test", "desc"));
        var prefix = task.Id.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate($"/reviews {prefix}"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No executions", sender.SentMessages[0].Text);
    }

    // --- /help includes M4 commands ---

    [Fact]
    public async Task HelpCommand_ListsReviewCommand()
    {
        var (handler, _, sender, _, _) = Build();

        await handler.HandleAsync(TextUpdate("/help"), CancellationToken.None);

        Assert.Contains("/review", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task HelpCommand_ListsReviewsCommand()
    {
        var (handler, _, sender, _, _) = Build();

        await handler.HandleAsync(TextUpdate("/help"), CancellationToken.None);

        Assert.Contains("/reviews", sender.SentMessages[0].Text);
    }
}
