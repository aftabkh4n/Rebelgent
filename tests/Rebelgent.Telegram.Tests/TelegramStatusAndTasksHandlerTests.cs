using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Telegram.Authorization;
using Rebelgent.Telegram.Handlers;
using Rebelgent.Telegram.Tests.Fakes;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramOptions = Rebelgent.Telegram.Options.TelegramOptions;

namespace Rebelgent.Telegram.Tests;

public class TelegramStatusAndTasksHandlerTests
{
    private const long AuthorizedUserId = 42L;
    private const long ChatId = 1000L;

    private (TelegramUpdateHandler handler, FakeTaskService taskService, FakeMessageSender sender) Build()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        var taskService = new FakeTaskService();
        var sender = new FakeMessageSender();
        var handler = new TelegramUpdateHandler(taskService, sender, auth, NullLogger<TelegramUpdateHandler>.Instance);
        return (handler, taskService, sender);
    }

    private TelegramUpdateHandler BuildWithThrowingService(out FakeMessageSender sender)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        sender = new FakeMessageSender();
        return new TelegramUpdateHandler(
            new FakeThrowingTaskService(), sender, auth,
            NullLogger<TelegramUpdateHandler>.Instance);
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

    // --- /status with stored tasks ---

    [Fact]
    public async Task Status_WithStoredTasks_SendsResponse()
    {
        var (handler, taskService, sender) = Build();
        await taskService.CreateTaskAsync(new CreateTaskInput("proj", "Task A", "desc"));

        await handler.HandleAsync(TextUpdate("/status"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
    }

    [Fact]
    public async Task Status_WithStoredTasks_IncludesTaskCount()
    {
        var (handler, taskService, sender) = Build();
        await taskService.CreateTaskAsync(new CreateTaskInput("proj", "Task A", "desc"));
        await taskService.CreateTaskAsync(new CreateTaskInput("proj", "Task B", "desc"));

        await handler.HandleAsync(TextUpdate("/status"), CancellationToken.None);

        Assert.Contains("Total tasks: 2", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task Status_WithNoTasks_SendsResponse()
    {
        var (handler, _, sender) = Build();

        await handler.HandleAsync(TextUpdate("/status"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Total tasks: 0", sender.SentMessages[0].Text);
    }

    // --- /tasks with stored tasks ---

    [Fact]
    public async Task Tasks_WithStoredTasks_SendsResponse()
    {
        var (handler, taskService, sender) = Build();
        await taskService.CreateTaskAsync(new CreateTaskInput("proj", "Fix login bug", "desc"));

        await handler.HandleAsync(TextUpdate("/tasks"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
    }

    [Fact]
    public async Task Tasks_WithStoredTasks_IncludesTaskTitle()
    {
        var (handler, taskService, sender) = Build();
        await taskService.CreateTaskAsync(new CreateTaskInput("proj", "Fix login bug", "desc"));

        await handler.HandleAsync(TextUpdate("/tasks"), CancellationToken.None);

        Assert.Contains("Fix login bug", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task Tasks_WithNoTasks_ReturnsNoTasksMessage()
    {
        var (handler, _, sender) = Build();

        await handler.HandleAsync(TextUpdate("/tasks"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("No tasks yet", sender.SentMessages[0].Text);
    }

    // --- Error handling: persistence failure must not silently drop the response ---

    [Fact]
    public async Task Status_WhenPersistenceFails_SendsErrorMessage()
    {
        var handler = BuildWithThrowingService(out var sender);

        await handler.HandleAsync(TextUpdate("/status"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Unable to retrieve tasks", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task Tasks_WhenPersistenceFails_SendsErrorMessage()
    {
        var handler = BuildWithThrowingService(out var sender);

        await handler.HandleAsync(TextUpdate("/tasks"), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Unable to retrieve tasks", sender.SentMessages[0].Text);
    }

    [Fact]
    public async Task Status_WhenPersistenceFails_DoesNotThrow()
    {
        var handler = BuildWithThrowingService(out _);

        var exception = await Record.ExceptionAsync(() =>
            handler.HandleAsync(TextUpdate("/status"), CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Tasks_WhenPersistenceFails_DoesNotThrow()
    {
        var handler = BuildWithThrowingService(out _);

        var exception = await Record.ExceptionAsync(() =>
            handler.HandleAsync(TextUpdate("/tasks"), CancellationToken.None));

        Assert.Null(exception);
    }
}
