using Microsoft.Extensions.Logging;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Orchestrator;
using Rebelgent.Orchestration.Projects;
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
    private readonly IProjectRegistry _projectRegistry;
    private readonly ITaskOrchestrator _orchestrator;
    private readonly ILogger<TelegramUpdateHandler> _logger;

    public TelegramUpdateHandler(
        ITaskService taskService,
        ITelegramMessageSender sender,
        TelegramAuthorizationService authorization,
        IProjectRegistry projectRegistry,
        ITaskOrchestrator orchestrator,
        ILogger<TelegramUpdateHandler> logger)
    {
        _taskService = taskService;
        _sender = sender;
        _authorization = authorization;
        _projectRegistry = projectRegistry;
        _orchestrator = orchestrator;
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
            await HandleCommandAsync(chatId, command, text, cancellationToken);
        }
        else
        {
            await HandleTaskRequestAsync(chatId, text, cancellationToken);
        }
    }

    private async Task HandleCommandAsync(long chatId, string command, string rawText, CancellationToken cancellationToken)
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
                    "/tasks — recent tasks\n" +
                    "/projects — list registered projects\n" +
                    "/task <projectId> <description> — create a task for a project\n" +
                    "/run <taskId> — run an agent on a task (requires human approval)\n" +
                    "/taskinfo <taskId> — show task details\n\n" +
                    "To create a task with the default project, send any non-command message.",
                    cancellationToken);
                break;

            case "/status":
                await HandleStatusAsync(chatId, cancellationToken);
                break;

            case "/tasks":
                await HandleTasksAsync(chatId, cancellationToken);
                break;

            case "/projects":
                await HandleProjectsAsync(chatId, cancellationToken);
                break;

            case "/task":
                await HandleTaskCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/run":
                await HandleRunCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/taskinfo":
                await HandleTaskInfoCommandAsync(chatId, rawText, cancellationToken);
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
                $"[{t.Id.ToString("N")[..8]}] {t.ProjectId} — {t.Title}\n" +
                $"  Status: {t.Status}  Role: {t.AssignedRole}");

            await _sender.SendTextAsync(chatId, string.Join("\n\n", lines), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve tasks for /tasks");
            await _sender.SendTextAsync(chatId, "Unable to retrieve tasks right now.", cancellationToken);
        }
    }

    private async Task HandleProjectsAsync(long chatId, CancellationToken cancellationToken)
    {
        var projects = _projectRegistry.GetAll();
        if (projects.Count == 0)
        {
            await _sender.SendTextAsync(chatId, "No projects are registered.", cancellationToken);
            return;
        }

        var lines = projects.Select(p => $"• {p.Id} — {p.Name}");
        await _sender.SendTextAsync(chatId, "Registered projects:\n" + string.Join("\n", lines), cancellationToken);
    }

    private async Task HandleTaskCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        // Expected: /task <projectId> <description>
        var parts = rawText.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            await _sender.SendTextAsync(chatId, "Usage: /task <projectId> <description>", cancellationToken);
            return;
        }

        var projectId = parts[1];
        var description = parts[2];

        var project = _projectRegistry.Find(projectId);
        if (project is null)
        {
            await _sender.SendTextAsync(chatId, $"Project '{projectId}' is not registered. Use /projects to see available projects.", cancellationToken);
            return;
        }

        var title = description.Length > 100 ? description[..100] : description;

        try
        {
            var task = await _taskService.CreateTaskAsync(
                new CreateTaskInput(project.Id, title, description),
                cancellationToken);

            await _sender.SendTextAsync(chatId,
                $"Task created\n\n" +
                $"ID: {task.Id.ToString("N")[..8]}\n" +
                $"Project: {project.Id} — {project.Name}\n" +
                $"Status: {task.Status}\n\n" +
                $"Use /run {task.Id.ToString("N")[..8]} to execute.",
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to create task for project {ProjectId}", projectId);
            await _sender.SendTextAsync(chatId, "Failed to create task. Please try again.", cancellationToken);
        }
    }

    private async Task HandleRunCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        // Expected: /run <taskId>
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /run <taskId>", cancellationToken);
            return;
        }

        var prefix = parts[1];
        var matches = await _taskService.FindByPrefixAsync(prefix, 2, cancellationToken);

        if (matches.Count == 0)
        {
            await _sender.SendTextAsync(chatId, $"No task found matching '{prefix}'.", cancellationToken);
            return;
        }

        if (matches.Count > 1)
        {
            await _sender.SendTextAsync(chatId, $"Ambiguous task ID '{prefix}'. Use more characters.", cancellationToken);
            return;
        }

        var task = matches.First();

        // Validate the project is registered before queuing
        if (_projectRegistry.Find(task.ProjectId) is null)
        {
            await _sender.SendTextAsync(chatId, $"Project '{task.ProjectId}' is not registered and cannot be executed.", cancellationToken);
            return;
        }

        await _sender.SendTextAsync(chatId,
            $"Running task [{task.Id.ToString("N")[..8]}] {task.Title}\n" +
            $"Project: {task.ProjectId}\n\n" +
            "Agent execution started. You will receive a result when it completes.",
            cancellationToken);

        // Fire-and-forget: capture singletons, not the scoped handler
        var taskId = task.Id;
        var orchestrator = _orchestrator;
        var sender = _sender;
        var logger = _logger;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await orchestrator.RunAsync(taskId, CancellationToken.None);
                var emoji = result.Succeeded ? "✅" : "❌";
                await sender.SendTextAsync(chatId, $"{emoji} {result.Summary}", CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception during orchestration for task {TaskId}", taskId);
                await sender.SendTextAsync(chatId, "An unexpected error occurred during agent execution.", CancellationToken.None);
            }
        });
    }

    private async Task HandleTaskInfoCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /taskinfo <taskId>", cancellationToken);
            return;
        }

        var prefix = parts[1];

        try
        {
            var matches = await _taskService.FindByPrefixAsync(prefix, 2, cancellationToken);

            if (matches.Count == 0)
            {
                await _sender.SendTextAsync(chatId, $"No task found matching '{prefix}'.", cancellationToken);
                return;
            }

            if (matches.Count > 1)
            {
                await _sender.SendTextAsync(chatId, $"Ambiguous task ID '{prefix}'. Use more characters.", cancellationToken);
                return;
            }

            var task = matches.First();
            var branchInfo = task.BranchName is not null ? $"\nBranch: {task.BranchName}" : string.Empty;

            await _sender.SendTextAsync(chatId,
                $"Task: {task.Title}\n" +
                $"ID: {task.Id.ToString("N")[..8]}\n" +
                $"Project: {task.ProjectId}\n" +
                $"Status: {task.Status}\n" +
                $"Created: {task.CreatedAt:yyyy-MM-dd HH:mm} UTC" +
                branchInfo,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve task info for prefix {Prefix}", prefix);
            await _sender.SendTextAsync(chatId, "Unable to retrieve task info right now.", cancellationToken);
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
            $"ID: {task.Id.ToString("N")[..8]}\n" +
            $"Status: {task.Status}\n" +
            $"Assigned to: {task.AssignedRole}\n\n" +
            $"Use /task <projectId> <description> to create a task for a registered project.",
            cancellationToken);
    }
}
