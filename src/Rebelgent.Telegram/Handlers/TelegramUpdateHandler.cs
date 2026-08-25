using Microsoft.Extensions.Logging;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Telegram.Authorization;
using Rebelgent.Telegram.Commands;
using Rebelgent.Telegram.Messaging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Rebelgent.Telegram.Handlers;

/// <summary>
/// Routes incoming Telegram updates to the appropriate handler.
/// Only authorized user IDs are processed; all others receive no response.
/// Business logic must not live here — this handler delegates to application services.
/// </summary>
public class TelegramUpdateHandler
{
    private readonly ITaskService _taskService;
    private readonly ITelegramMessageSender _sender;
    private readonly TelegramAuthorizationService _authorization;
    private readonly ILogger<TelegramUpdateHandler> _logger;

    public TelegramUpdateHandler(
        ITaskService taskService,
        ITelegramMessageSender sender,
        TelegramAuthorizationService authorization,
        ILogger<TelegramUpdateHandler> logger)
    {
        _taskService = taskService;
        _sender = sender;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task HandleAsync(Update update, CancellationToken cancellationToken)
    {
        if (update.Type != UpdateType.Message || update.Message?.Text is null)
            return;

        var message = update.Message;
        var userId = message.From?.Id;
        var chatId = message.Chat.Id;
        var text = message.Text.Trim();

        if (userId is null || !_authorization.IsAuthorized(userId.Value))
            return;

        if (TelegramCommandParser.TryParse(text, out var command))
        {
            await HandleCommandAsync(chatId, command, cancellationToken);
        }
        else
        {
            await HandleTaskRequestAsync(chatId, text, cancellationToken);
        }
    }

    private async Task HandleCommandAsync(long chatId, string command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case "/start":
                await _sender.SendTextAsync(chatId,
                    "Rebelgent is online. Send me a software task or use /help.",
                    cancellationToken);
                break;

            case "/help":
                await _sender.SendTextAsync(chatId,
                    "Commands:\n" +
                    "/start — introduction\n" +
                    "/help — show this list\n" +
                    "/status — system status\n" +
                    "/tasks — recent tasks\n\n" +
                    "To create a task, send any non-command message.",
                    cancellationToken);
                break;

            case "/status":
                await HandleStatusAsync(chatId, cancellationToken);
                break;

            case "/tasks":
                await HandleTasksAsync(chatId, cancellationToken);
                break;

            default:
                await _sender.SendTextAsync(chatId,
                    $"Unknown command: {command}. Use /help to see available commands.",
                    cancellationToken);
                break;
        }
    }

    private async Task HandleStatusAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var recent = await _taskService.GetRecentTasksAsync(100, cancellationToken);
            var active = recent.Count(t => t.Status is not (AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled));

            await _sender.SendTextAsync(chatId,
                $"Rebelgent — online\n" +
                $"Total tasks: {recent.Count}\n" +
                $"Active tasks: {active}",
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve tasks for /status");
            await _sender.SendTextAsync(chatId, "Unable to retrieve tasks right now.", cancellationToken);
        }
    }

    private async Task HandleTasksAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var tasks = await _taskService.GetRecentTasksAsync(10, cancellationToken);

            if (tasks.Count == 0)
            {
                await _sender.SendTextAsync(chatId, "No tasks yet.", cancellationToken);
                return;
            }

            var lines = tasks.Select(t =>
                $"[{t.Id.ToString()[..8]}] {t.ProjectId} — {t.Title}\n" +
                $"  Status: {t.Status}  Role: {t.AssignedRole}");

            await _sender.SendTextAsync(chatId, string.Join("\n\n", lines), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve tasks for /tasks");
            await _sender.SendTextAsync(chatId, "Unable to retrieve tasks right now.", cancellationToken);
        }
    }

    private async Task HandleTaskRequestAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        var title = text.Length > 100 ? text[..100] : text;

        var task = await _taskService.CreateTaskAsync(
            new CreateTaskInput("default", title, text),
            cancellationToken);

        await _sender.SendTextAsync(chatId,
            $"Task created\n\n" +
            $"ID: {task.Id.ToString()[..8]}\n" +
            $"Status: {task.Status}\n" +
            $"Assigned to: {task.AssignedRole}\n\n" +
            "Rebelgent has recorded the request. Agent execution is not enabled yet.",
            cancellationToken);
    }
}
