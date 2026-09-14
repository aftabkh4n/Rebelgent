using Microsoft.Extensions.Logging;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.GitHub;
using Rebelgent.GitHub.Package;
using Rebelgent.GitHub.Release;
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
    private readonly IQualityOrchestrator _qualityOrchestrator;
    private readonly IPullRequestOrchestrator _pullRequestOrchestrator;
    private readonly IMergeOrchestrator _mergeOrchestrator;
    private readonly IReleaseOrchestrator _releaseOrchestrator;
    private readonly IPackageOrchestrator _packageOrchestrator;
    private readonly IAgentExecutionRepository _executionRepository;
    private readonly ILogger<TelegramUpdateHandler> _logger;

    public TelegramUpdateHandler(
        ITaskService taskService,
        ITelegramMessageSender sender,
        TelegramAuthorizationService authorization,
        IProjectRegistry projectRegistry,
        ITaskOrchestrator orchestrator,
        IQualityOrchestrator qualityOrchestrator,
        IPullRequestOrchestrator pullRequestOrchestrator,
        IMergeOrchestrator mergeOrchestrator,
        IReleaseOrchestrator releaseOrchestrator,
        IPackageOrchestrator packageOrchestrator,
        IAgentExecutionRepository executionRepository,
        ILogger<TelegramUpdateHandler> logger)
    {
        _taskService = taskService;
        _sender = sender;
        _authorization = authorization;
        _projectRegistry = projectRegistry;
        _orchestrator = orchestrator;
        _qualityOrchestrator = qualityOrchestrator;
        _pullRequestOrchestrator = pullRequestOrchestrator;
        _mergeOrchestrator = mergeOrchestrator;
        _releaseOrchestrator = releaseOrchestrator;
        _packageOrchestrator = packageOrchestrator;
        _executionRepository = executionRepository;
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
                    "/run <taskId> — run the developer agent on a task\n" +
                    "/taskinfo <taskId> — show task details\n" +
                    "/review <taskId> — trigger QA and code review pipeline\n" +
                    "/review <taskId> <approve|reject|rerun> — human decision after review\n" +
                    "/reviews <taskId> — show QA and review findings for a task\n" +
                    "/pr <taskId> — push developer branch and create GitHub pull request\n" +
                    "/prinfo <taskId> — show pull request info for a task\n" +
                    "/merge <taskId> — merge the pull request for a task\n" +
                    "/mergeinfo <taskId> — show merge info for a task\n" +
                    "/release <taskId> — prepare release notes (runs Release Manager)\n" +
                    "/release <taskId> approve — create the GitHub release\n" +
                    "/releaseinfo <taskId> — show release info for a task\n" +
                    "/package <taskId> — prepare NuGet package (requires published GitHub release)\n" +
                    "/package <taskId> approve — publish the package to NuGet (requires human approval)\n" +
                    "/packageinfo <taskId> — show package info for a task\n\n" +
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

            case "/review":
                await HandleReviewCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/reviews":
                await HandleReviewsCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/pr":
                await HandlePrCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/prinfo":
                await HandlePrInfoCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/merge":
                await HandleMergeCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/mergeinfo":
                await HandleMergeInfoCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/release":
                await HandleReleaseCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/releaseinfo":
                await HandleReleaseInfoCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/package":
                await HandlePackageCommandAsync(chatId, rawText, cancellationToken);
                break;

            case "/packageinfo":
                await HandlePackageInfoCommandAsync(chatId, rawText, cancellationToken);
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
                await sender.SendTextAsync(chatId,
                    $"{emoji} {result.Summary}\n\nUse /review {taskId.ToString("N")[..8]} to run QA and code review.",
                    CancellationToken.None);
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

    private async Task HandleReviewCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        // /review <taskId>              — trigger QA + code review pipeline
        // /review <taskId> approve      — human approves, marks Completed
        // /review <taskId> reject       — human rejects, marks Failed
        // /review <taskId> rerun        — re-trigger QA + code review
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId,
                "Usage: /review <taskId> — trigger QA and code review\n" +
                "       /review <taskId> <approve|reject|rerun> — human decision after review",
                cancellationToken);
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

        if (parts.Length == 2)
        {
            // 2-arg: trigger QA + review pipeline
            await _sender.SendTextAsync(chatId,
                $"Starting QA and Code Review for task [{task.Id.ToString("N")[..8]}] {task.Title}...\n" +
                "You will receive a result when it completes.",
                cancellationToken);

            var taskId = task.Id;
            var qualityOrchestrator = _qualityOrchestrator;
            var sender = _sender;
            var logger = _logger;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await qualityOrchestrator.RunAsync(taskId, CancellationToken.None);
                    var emoji = result.Succeeded ? "✅" : "⚠️";
                    await sender.SendTextAsync(chatId, $"{emoji} {result.Summary}", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled exception during quality orchestration for task {TaskId}", taskId);
                    await sender.SendTextAsync(chatId, "An unexpected error occurred during quality review.", CancellationToken.None);
                }
            });
            return;
        }

        // 3-arg: human decision
        var decision = parts[2].ToLowerInvariant();
        if (decision is not ("approve" or "reject" or "rerun"))
        {
            await _sender.SendTextAsync(chatId, "Decision must be: approve, reject, or rerun", cancellationToken);
            return;
        }

        switch (decision)
        {
            case "approve":
                await _taskService.TransitionAsync(task.Id, AgentTaskStatus.Completed, cancellationToken);
                await _sender.SendTextAsync(chatId,
                    $"Task [{task.Id.ToString("N")[..8]}] approved and marked Completed.\n" +
                    $"Branch: {task.BranchName ?? "(none)"}\n" +
                    "Create a pull request manually when ready.",
                    cancellationToken);
                break;

            case "reject":
                await _taskService.TransitionAsync(task.Id, AgentTaskStatus.Failed, cancellationToken);
                await _sender.SendTextAsync(chatId,
                    $"Task [{task.Id.ToString("N")[..8]}] rejected and marked Failed.",
                    cancellationToken);
                break;

            case "rerun":
                await _sender.SendTextAsync(chatId,
                    $"Triggering QA + Code Review for task [{task.Id.ToString("N")[..8]}]...",
                    cancellationToken);

                var rerunTaskId = task.Id;
                var rerunQualityOrchestrator = _qualityOrchestrator;
                var rerunSender = _sender;
                var rerunLogger = _logger;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await rerunQualityOrchestrator.RunAsync(rerunTaskId, CancellationToken.None);
                        var emoji = result.Succeeded ? "✅" : "⚠️";
                        await rerunSender.SendTextAsync(chatId, $"{emoji} {result.Summary}", CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        rerunLogger.LogError(ex, "Unhandled exception during quality orchestration for task {TaskId}", rerunTaskId);
                        await rerunSender.SendTextAsync(chatId, "An unexpected error occurred during quality review.", CancellationToken.None);
                    }
                });
                break;
        }
    }

    private async Task HandleReviewsCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        // Expected: /reviews <taskId>
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /reviews <taskId>", cancellationToken);
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

        try
        {
            var executions = await _executionRepository.GetAllByTaskIdAsync(task.Id, cancellationToken);
            if (executions.Count == 0)
            {
                await _sender.SendTextAsync(chatId, $"No executions found for task [{task.Id.ToString("N")[..8]}].", cancellationToken);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Executions for task [{task.Id.ToString("N")[..8]}] {task.Title}:");
            sb.AppendLine();

            foreach (var exec in executions)
            {
                sb.AppendLine($"Role: {exec.Role} | Status: {exec.Status} | Provider: {exec.Provider}");
                sb.AppendLine($"Started: {exec.StartedAt:yyyy-MM-dd HH:mm} UTC");
                if (!string.IsNullOrWhiteSpace(exec.Findings))
                    sb.AppendLine($"Findings: {exec.Findings[..Math.Min(500, exec.Findings.Length)]}");
                if (!string.IsNullOrWhiteSpace(exec.ErrorMessage))
                    sb.AppendLine($"Error: {exec.ErrorMessage}");
                sb.AppendLine();
            }

            await _sender.SendTextAsync(chatId, sb.ToString().TrimEnd(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve executions for task prefix {Prefix}", prefix);
            await _sender.SendTextAsync(chatId, "Unable to retrieve execution history right now.", cancellationToken);
        }
    }

    private async Task HandlePrCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /pr <taskId>", cancellationToken);
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
        await _sender.SendTextAsync(chatId,
            $"Creating pull request for task [{task.Id.ToString("N")[..8]}] {task.Title}...\n" +
            "You will receive a result when it completes.",
            cancellationToken);

        var taskId = task.Id;
        var orchestrator = _pullRequestOrchestrator;
        var sender = _sender;
        var logger = _logger;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await orchestrator.RunAsync(taskId, CancellationToken.None);
                var symbol = result.Succeeded ? "✅" : "❌";
                await sender.SendTextAsync(chatId, $"{symbol} {result.Summary}", CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception during PR creation for task {TaskId}", taskId);
                await sender.SendTextAsync(chatId, "An unexpected error occurred during PR creation.", CancellationToken.None);
            }
        });
    }

    private async Task HandlePrInfoCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /prinfo <taskId>", cancellationToken);
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
        var shortId = task.Id.ToString("N")[..8];

        if (task.PullRequestNumber is null)
        {
            await _sender.SendTextAsync(chatId,
                $"Task [{shortId}] {task.Title}\nNo pull request created yet. Use /pr {shortId} to create one.",
                cancellationToken);
            return;
        }

        await _sender.SendTextAsync(chatId,
            $"Task [{shortId}] {task.Title}\n" +
            $"PR #{task.PullRequestNumber}: {task.PullRequestUrl}\n" +
            $"Created: {task.PullRequestCreatedAt:yyyy-MM-dd HH:mm} UTC",
            cancellationToken);
    }

    private async Task HandleReleaseCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        // /release <taskId>           — prepare release (runs Release Manager agent)
        // /release <taskId> approve   — approve and create GitHub Release
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /release <taskId> [approve]", cancellationToken);
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
        var isApprove = parts.Length >= 3 && string.Equals(parts[2], "approve", StringComparison.OrdinalIgnoreCase);

        if (isApprove)
        {
            await _sender.SendTextAsync(chatId,
                $"Creating GitHub Release for task [{task.Id.ToString("N")[..8]}] {task.Title}...\n" +
                "You will receive a result when it completes.",
                cancellationToken);

            var taskId = task.Id;
            var orchestrator = _releaseOrchestrator;
            var sender = _sender;
            var logger = _logger;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await orchestrator.ApproveAndPublishAsync(taskId, CancellationToken.None);
                    var symbol = result.Succeeded ? "✅" : "❌";
                    await sender.SendTextAsync(chatId, $"{symbol} {result.Summary}", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled exception during release publish for task {TaskId}", taskId);
                    await sender.SendTextAsync(chatId, "An unexpected error occurred during release publishing.", CancellationToken.None);
                }
            });
        }
        else
        {
            await _sender.SendTextAsync(chatId,
                $"Running Release Manager for task [{task.Id.ToString("N")[..8]}] {task.Title}...\n" +
                "You will receive a result when it completes.",
                cancellationToken);

            var taskId = task.Id;
            var orchestrator = _releaseOrchestrator;
            var sender = _sender;
            var logger = _logger;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await orchestrator.PrepareAsync(taskId, CancellationToken.None);
                    var symbol = result.Succeeded ? "✅" : "❌";
                    await sender.SendTextAsync(chatId, $"{symbol} {result.Summary}", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled exception during release preparation for task {TaskId}", taskId);
                    await sender.SendTextAsync(chatId, "An unexpected error occurred during release preparation.", CancellationToken.None);
                }
            });
        }
    }

    private async Task HandleReleaseInfoCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /releaseinfo <taskId>", cancellationToken);
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
        var shortId = task.Id.ToString("N")[..8];

        // We need the release from the repository — ask via task prefix lookup + orchestrator info
        // The handler doesn't have direct repository access; use a dedicated info path via the orchestrator
        // We expose a lightweight info command using the orchestrator's PrepareAsync idempotency
        // but that would re-run the agent. Instead, store release info in a dedicated query.
        // For now, we call PrepareAsync which is idempotent when already prepared.
        try
        {
            // The orchestrator is idempotent — if prepared, it returns the existing record without re-running
            var result = await _releaseOrchestrator.PrepareAsync(task.Id, cancellationToken);

            if (!result.Succeeded || result.Status is null)
            {
                await _sender.SendTextAsync(chatId,
                    $"Task [{shortId}] {task.Title}\nNo release prepared yet. Use /release {shortId} to prepare.",
                    cancellationToken);
                return;
            }

            var urlLine = result.GitHubReleaseUrl is not null ? $"\nURL: {result.GitHubReleaseUrl}" : string.Empty;
            await _sender.SendTextAsync(chatId,
                $"Task [{shortId}] {task.Title}\n" +
                $"Version: {result.Version}\n" +
                $"Tag: {result.TagName}\n" +
                $"Title: {result.Title}\n" +
                $"Status: {result.Status}" +
                urlLine,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve release info for task prefix {Prefix}", prefix);
            await _sender.SendTextAsync(chatId, "Unable to retrieve release info right now.", cancellationToken);
        }
    }

    private async Task HandleMergeCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /merge <taskId>", cancellationToken);
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
        await _sender.SendTextAsync(chatId,
            $"Merging pull request for task [{task.Id.ToString("N")[..8]}] {task.Title}...\n" +
            "You will receive a result when it completes.",
            cancellationToken);

        var taskId = task.Id;
        var orchestrator = _mergeOrchestrator;
        var sender = _sender;
        var logger = _logger;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await orchestrator.RunAsync(taskId, CancellationToken.None);
                var symbol = result.Succeeded ? "✅" : "❌";
                await sender.SendTextAsync(chatId, $"{symbol} {result.Summary}", CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception during merge for task {TaskId}", taskId);
                await sender.SendTextAsync(chatId, "An unexpected error occurred during merge.", CancellationToken.None);
            }
        });
    }

    private async Task HandleMergeInfoCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /mergeinfo <taskId>", cancellationToken);
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
        var shortId = task.Id.ToString("N")[..8];

        if (task.MergeCommitSha is null)
        {
            await _sender.SendTextAsync(chatId,
                $"Task [{shortId}] {task.Title}\nNot yet merged. Use /merge {shortId} to merge the pull request.",
                cancellationToken);
            return;
        }

        await _sender.SendTextAsync(chatId,
            $"Task [{shortId}] {task.Title}\n" +
            $"Merge method: {task.MergeMethod}\n" +
            $"Commit: {task.MergeCommitSha}\n" +
            $"Merged: {task.MergedAt:yyyy-MM-dd HH:mm} UTC",
            cancellationToken);
    }

    private async Task HandlePackageCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        // /package <taskId>           — prepare package (dotnet pack)
        // /package <taskId> approve   — approve and publish to NuGet
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /package <taskId> [approve]", cancellationToken);
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
        var isApprove = parts.Length >= 3 && string.Equals(parts[2], "approve", StringComparison.OrdinalIgnoreCase);

        if (isApprove)
        {
            await _sender.SendTextAsync(chatId,
                $"Publishing package for task [{task.Id.ToString("N")[..8]}] {task.Title}...\n" +
                "You will receive a result when it completes.",
                cancellationToken);

            var taskId = task.Id;
            var orchestrator = _packageOrchestrator;
            var sender = _sender;
            var logger = _logger;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await orchestrator.ApproveAndPublishAsync(taskId, CancellationToken.None);
                    var symbol = result.Succeeded ? "✅" : "❌";
                    await sender.SendTextAsync(chatId, $"{symbol} {result.Summary}", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled exception during package publish for task {TaskId}", taskId);
                    await sender.SendTextAsync(chatId, "An unexpected error occurred during package publishing.", CancellationToken.None);
                }
            });
        }
        else
        {
            await _sender.SendTextAsync(chatId,
                $"Preparing package for task [{task.Id.ToString("N")[..8]}] {task.Title}...\n" +
                "You will receive a result when it completes.",
                cancellationToken);

            var taskId = task.Id;
            var orchestrator = _packageOrchestrator;
            var sender = _sender;
            var logger = _logger;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await orchestrator.PrepareAsync(taskId, CancellationToken.None);
                    var symbol = result.Succeeded ? "✅" : "❌";
                    await sender.SendTextAsync(chatId, $"{symbol} {result.Summary}", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled exception during package preparation for task {TaskId}", taskId);
                    await sender.SendTextAsync(chatId, "An unexpected error occurred during package preparation.", CancellationToken.None);
                }
            });
        }
    }

    private async Task HandlePackageInfoCommandAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await _sender.SendTextAsync(chatId, "Usage: /packageinfo <taskId>", cancellationToken);
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
        var shortId = task.Id.ToString("N")[..8];

        try
        {
            var result = await _packageOrchestrator.GetInfoAsync(task.Id, cancellationToken);

            if (!result.Succeeded)
            {
                await _sender.SendTextAsync(chatId,
                    $"Task [{shortId}] {task.Title}\nNo package prepared yet. Use /package {shortId} to prepare.",
                    cancellationToken);
                return;
            }

            var publishedLine = result.PublishedAt.HasValue
                ? $"\nPublished: {result.PublishedAt.Value:yyyy-MM-dd HH:mm} UTC"
                : string.Empty;

            await _sender.SendTextAsync(chatId,
                $"Task [{shortId}] {task.Title}\n" +
                $"Package: {result.PackageId} {result.PackageVersion}\n" +
                $"Status: {result.Status}\n" +
                $"Prepared: {result.PreparedAt:yyyy-MM-dd HH:mm} UTC" +
                publishedLine,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve package info for task prefix {Prefix}", prefix);
            await _sender.SendTextAsync(chatId, "Unable to retrieve package info right now.", cancellationToken);
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
