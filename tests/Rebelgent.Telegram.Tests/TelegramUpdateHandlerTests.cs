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

public class TelegramUpdateHandlerTests
{
    private const long AuthorizedUserId = 42L;
    private const long UnauthorizedUserId = 99L;
    private const long ChatId = 1000L;

    private (TelegramUpdateHandler handler, FakeTaskService taskService, FakeMessageSender sender) Build(
        IEnumerable<ProjectDefinition>? projects = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        var taskService = new FakeTaskService();
        var sender = new FakeMessageSender();
        var projectRegistry = new FakeProjectRegistry(projects);
        var orchestrator = new FakeTaskOrchestrator();
        var handler = new TelegramUpdateHandler(taskService, sender, auth, projectRegistry, orchestrator,
            new FakeQualityOrchestrator(), new FakePullRequestOrchestrator(), new FakeMergeOrchestrator(), new FakeReleaseOrchestrator(), new FakePackageOrchestrator(), new FakeImprovementOrchestrator(), new FakeExecutionRepository(),
            new FakeAgentLifecycleService(), new FakeAgentEvolutionOrchestrator(), new FakeAgentEvolutionProposalRepository(), new FakeAuditRepository(), new FakeAuditLedgerVerifier(), new FakeSecurityAuditDeadLetterRepository(), new FakeAuditRecoveryService(), new FakeScopedBackgroundExecutor(), new TelegramHumanPrincipalFactory(),
            NullLogger<TelegramUpdateHandler>.Instance);
        return (handler, taskService, sender);
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

    // --- Authorization ---

    [Fact]
    public async Task UnauthorizedUser_NaturalLanguage_DoesNotCreateTask()
    {
        var (handler, taskService, _) = Build();
        var update = TextUpdate(UnauthorizedUserId, ChatId, "Add search feature");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    [Fact]
    public async Task UnauthorizedUser_NaturalLanguage_ReceivesNoResponse()
    {
        var (handler, _, sender) = Build();
        var update = TextUpdate(UnauthorizedUserId, ChatId, "Add search feature");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(sender.SentMessages);
    }

    // --- Natural language task creation ---

    [Fact]
    public async Task AuthorizedUser_NaturalLanguage_CreatesOneTask()
    {
        var (handler, taskService, _) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, "Add memory expiration support to BlazorMemory");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Single(taskService.CreatedTasks);
    }

    [Fact]
    public async Task AuthorizedUser_NaturalLanguage_SendsCreatedResponse()
    {
        var (handler, _, sender) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, "Add memory expiration support");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Task created", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task RunCommand_FailedTask_RefusesAndRecommendsRetryWithoutStarting()
    {
        var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = "D:\\Projects\\Sandbox" };
        var (handler, taskService, sender) = Build([project]);
        var task = new AgentTask("sandbox", "Failed task", "Previous attempt", AgentRole.BackendDeveloper);
        var lifecycle = new TaskLifecycleService();
        lifecycle.Transition(task, AgentTaskStatus.Planning);
        lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        lifecycle.Transition(task, AgentTaskStatus.Approved);
        lifecycle.Transition(task, AgentTaskStatus.InProgress);
        lifecycle.Transition(task, AgentTaskStatus.Failed);
        taskService.SeededTasks.Add(task);

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, ChatId, $"/run {task.Id:N}"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("previously failed", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/retry", sender.SentMessages[0].Text);
        Assert.DoesNotContain("Agent execution started", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task RunCommand_PlanningTask_RefusesWithoutClaimingExecutionStarted()
    {
        var (handler, service, sender) = Build();
        var task = new AgentTask("sandbox", "Planning", "Normal planning", AgentRole.BackendDeveloper);
        new TaskLifecycleService().Transition(task, AgentTaskStatus.Planning);
        service.SeededTasks.Add(task);
        await handler.HandleAsync(TextUpdate(AuthorizedUserId, ChatId, $"/run {task.Id:N}"), CancellationToken.None);
        Assert.Single(sender.SentMessages);
        Assert.Contains("already in Planning", sender.SentMessages[0].Text);
        Assert.DoesNotContain("Agent execution started", sender.SentMessages[0].Text);
        Assert.Equal(AgentTaskStatus.Planning, task.Status);
    }

    [Fact]
    public async Task RetryCommand_FailedTask_UsesExplicitRetryFlow()
    {
        var project = new ProjectDefinition { Id = "sandbox", Name = "Sandbox", RepositoryPath = "D:\\Projects\\Sandbox" };
        var (handler, taskService, sender) = Build([project]);
        var task = new AgentTask("sandbox", "Failed task", "Previous attempt", AgentRole.BackendDeveloper);
        var lifecycle = new TaskLifecycleService();
        lifecycle.Transition(task, AgentTaskStatus.Planning);
        lifecycle.Transition(task, AgentTaskStatus.AwaitingApproval);
        lifecycle.Transition(task, AgentTaskStatus.Approved);
        lifecycle.Transition(task, AgentTaskStatus.InProgress);
        lifecycle.Transition(task, AgentTaskStatus.Failed);
        taskService.SeededTasks.Add(task);

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, ChatId, $"/retry {task.Id:N}"), CancellationToken.None);

        Assert.NotEmpty(sender.SentMessages);
        Assert.Contains("Retry requested", sender.SentMessages[0].Text);
        Assert.DoesNotContain("Attempt:", sender.SentMessages[0].Text);
        Assert.DoesNotContain("Agent execution started", sender.SentMessages[0].Text);
    }

    // --- Commands do not create tasks ---

    [Fact]
    public async Task StatusCommand_DoesNotCreateTask()
    {
        var (handler, taskService, _) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, "/status");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    [Fact]
    public async Task TasksCommand_DoesNotCreateTask()
    {
        var (handler, taskService, _) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, "/tasks");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    [Fact]
    public async Task StartCommand_DoesNotCreateTask()
    {
        var (handler, taskService, _) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, "/start");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    [Fact]
    public async Task HelpCommand_DoesNotCreateTask()
    {
        var (handler, taskService, _) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, "/help");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    // --- Correct chat routing ---

    [Fact]
    public async Task Response_SentToCorrectChat()
    {
        var (handler, _, sender) = Build();
        var update = TextUpdate(AuthorizedUserId, 9999L, "Build something");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Equal(9999L, sender.SentMessages[0].ChatId);
    }

    // --- Non-message updates are ignored ---

    [Fact]
    public async Task NonMessageUpdate_IsIgnored()
    {
        var (handler, taskService, sender) = Build();
        var update = new Update { Id = 1 }; // no Message

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
        Assert.Empty(sender.SentMessages);
    }

    // --- @botname suffix routing (the runtime bug) ---

    [Theory]
    [InlineData("/status@RebelgentBot")]
    [InlineData("/tasks@RebelgentBot")]
    [InlineData("/start@RebelgentBot")]
    [InlineData("/help@RebelgentBot")]
    public async Task BotnameSuffixCommand_DoesNotCreateTask(string commandText)
    {
        var (handler, taskService, _) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, commandText);

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    [Theory]
    [InlineData("/status@RebelgentBot")]
    [InlineData("/tasks@RebelgentBot")]
    public async Task BotnameSuffixCommand_SendsResponse(string commandText)
    {
        var (handler, _, sender) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, commandText);

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Single(sender.SentMessages);
    }

    // --- Unknown slash commands ---

    [Fact]
    public async Task UnknownSlashCommand_DoesNotCreateTask()
    {
        var (handler, taskService, _) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, "/whatever");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    [Fact]
    public async Task UnknownSlashCommand_SendsUnknownCommandResponse()
    {
        var (handler, _, sender) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, "/whatever");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Unknown command", sender.SentMessages[0].Text);
    }

    // --- Whitespace tolerance ---

    [Fact]
    public async Task StatusCommandWithWhitespace_DoesNotCreateTask()
    {
        var (handler, taskService, _) = Build();
        var update = TextUpdate(AuthorizedUserId, ChatId, "  /status  ");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    // --- Unauthorized user with @botname command ---

    [Fact]
    public async Task UnauthorizedUser_BotnameSuffixCommand_CreatesNoTaskAndSendsNoResponse()
    {
        var (handler, taskService, sender) = Build();
        var update = TextUpdate(UnauthorizedUserId, ChatId, "/status@RebelgentBot");

        await handler.HandleAsync(update, CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
        Assert.Empty(sender.SentMessages);
    }
}
