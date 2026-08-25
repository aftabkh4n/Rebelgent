using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    private (TelegramUpdateHandler handler, FakeTaskService taskService, FakeMessageSender sender) Build()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { AllowedUserIds = [AuthorizedUserId] });
        var auth = new TelegramAuthorizationService(options, NullLogger<TelegramAuthorizationService>.Instance);
        var taskService = new FakeTaskService();
        var sender = new FakeMessageSender();
        var handler = new TelegramUpdateHandler(taskService, sender, auth, NullLogger<TelegramUpdateHandler>.Instance);
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
