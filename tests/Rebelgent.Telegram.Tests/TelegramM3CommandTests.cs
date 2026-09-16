using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Telegram.Authorization;
using Rebelgent.Telegram.Handlers;
using Rebelgent.Telegram.Tests.Fakes;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramOptions = Rebelgent.Telegram.Options.TelegramOptions;

namespace Rebelgent.Telegram.Tests;

public class TelegramM3CommandTests
{
    private const long AuthorizedUserId = 42L;
    private const long ChatId = 1000L;

    private static readonly ProjectDefinition SandboxProject = new()
    {
        Id = "sandbox",
        Name = "RebelgentSandbox",
        RepositoryPath = @"D:\Projects\RebelgentSandbox",
        DefaultBranch = "main"
    };

    private (TelegramUpdateHandler handler, FakeTaskService taskService, FakeMessageSender sender, FakeTaskOrchestrator orchestrator) Build(
        IEnumerable<ProjectDefinition>? projects = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        var taskService = new FakeTaskService();
        var sender = new FakeMessageSender();
        var projectRegistry = new FakeProjectRegistry(projects ?? [SandboxProject]);
        var orchestrator = new FakeTaskOrchestrator();
        var handler = new TelegramUpdateHandler(taskService, sender, auth, projectRegistry, orchestrator,
            new FakeQualityOrchestrator(), new FakePullRequestOrchestrator(), new FakeMergeOrchestrator(), new FakeReleaseOrchestrator(), new FakePackageOrchestrator(), new FakeImprovementOrchestrator(), new FakeExecutionRepository(), NullLogger<TelegramUpdateHandler>.Instance);
        return (handler, taskService, sender, orchestrator);
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

    // --- /projects ---

    [Fact]
    public async Task ProjectsCommand_NoProjects_SendsNoProjectsMessage()
    {
        var (handler, _, sender, _) = Build([]);

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/projects"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No projects", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ProjectsCommand_WithProject_SendsProjectList()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/projects"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("sandbox", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task ProjectsCommand_DoesNotCreateTask()
    {
        var (handler, taskService, _, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/projects"), CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    // --- /task ---

    [Fact]
    public async Task TaskCommand_ValidProject_CreatesTask()
    {
        var (handler, taskService, _, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/task sandbox Add login feature"), CancellationToken.None);

        Assert.Single(taskService.CreatedTasks);
    }

    [Fact]
    public async Task TaskCommand_ValidProject_SendsCreatedResponse()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/task sandbox Add login feature"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Task created", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task TaskCommand_ValidProject_TaskHasCorrectProjectId()
    {
        var (handler, taskService, _, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/task sandbox Build search"), CancellationToken.None);

        Assert.Equal("sandbox", taskService.CreatedTasks[0].ProjectId);
    }

    [Fact]
    public async Task TaskCommand_UnknownProject_DoesNotCreateTask()
    {
        var (handler, taskService, _, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/task unknown Build search"), CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
    }

    [Fact]
    public async Task TaskCommand_UnknownProject_SendsNotRegisteredMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/task unknown Build search"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("not registered", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task TaskCommand_MissingArguments_SendsUsageMessage()
    {
        var (handler, taskService, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/task"), CancellationToken.None);

        Assert.Empty(taskService.CreatedTasks);
        Assert.Single(sender.SentMessages);
        Assert.Contains("Usage:", sender.SentMessages[0].Text);
    }

    // --- /taskinfo ---

    [Fact]
    public async Task TaskInfoCommand_NoArguments_SendsUsageMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/taskinfo"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Usage:", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task TaskInfoCommand_MatchingTask_SendsTaskDetails()
    {
        var (handler, taskService, sender, _) = Build();
        var task = await taskService.CreateTaskAsync(new Core.Services.CreateTaskInput("sandbox", "Fix auth bug", "desc"));
        var prefix = task.Id.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, $"/taskinfo {prefix}"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Fix auth bug", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task TaskInfoCommand_NoMatchingTask_SendsNotFoundMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/taskinfo 00000000"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No task found", sender.SentMessages[0].Text);
    }

    // --- /run ---

    [Fact]
    public async Task RunCommand_NoArguments_SendsUsageMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/run"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Usage:", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task RunCommand_NoMatchingTask_SendsNotFoundMessage()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/run 00000000"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No task found", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task RunCommand_UnregisteredProject_SendsErrorMessage()
    {
        var (handler, taskService, sender, _) = Build([]);
        // Create task with unregistered project
        var task = await taskService.CreateTaskAsync(new Core.Services.CreateTaskInput("orphaned-project", "Title", "desc"));
        var prefix = task.Id.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, $"/run {prefix}"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("not registered", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task RunCommand_ValidTask_SendsStartedMessage()
    {
        var (handler, taskService, sender, _) = Build();
        var task = await taskService.CreateTaskAsync(new Core.Services.CreateTaskInput("sandbox", "Build feature", "desc"));
        var prefix = task.Id.ToString("N")[..8];

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, $"/run {prefix}"), CancellationToken.None);

        Assert.NotEmpty(sender.SentMessages);
        Assert.Contains("execution started", sender.SentMessages[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    // --- /help includes M3 commands ---

    [Fact]
    public async Task HelpCommand_ListsProjectsCommand()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/help"), CancellationToken.None);

        Assert.Contains("/projects", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task HelpCommand_ListsTaskCommand()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/help"), CancellationToken.None);

        Assert.Contains("/task", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task HelpCommand_ListsRunCommand()
    {
        var (handler, _, sender, _) = Build();

        await handler.HandleAsync(TextUpdate(AuthorizedUserId, "/help"), CancellationToken.None);

        Assert.Contains("/run", sender.SentMessages[0].Text);
    }
}
