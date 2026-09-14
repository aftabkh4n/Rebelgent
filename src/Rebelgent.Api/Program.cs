using Microsoft.EntityFrameworkCore;
using Rebelgent.ClaudeCode.DependencyInjection;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.GitHub.DependencyInjection;
using Rebelgent.Contracts.Requests;
using Rebelgent.Contracts.Responses;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Services;
using Rebelgent.Infrastructure.DependencyInjection;
using Rebelgent.Orchestration.DependencyInjection;
using Rebelgent.Orchestration.Options;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Persistence;
using Rebelgent.Persistence.Options;
using Rebelgent.Telegram;
using Rebelgent.Telegram.Options;

var builder = WebApplication.CreateBuilder(args);

// Options
builder.Services.Configure<PersistenceOptions>(
    builder.Configuration.GetSection(PersistenceOptions.SectionName));
builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<WorkspaceOptions>(
    builder.Configuration.GetSection(WorkspaceOptions.SectionName));
builder.Services.Configure<ExecutionOptions>(
    builder.Configuration.GetSection(ExecutionOptions.SectionName));
builder.Services.Configure<ProjectRegistryOptions>(
    builder.Configuration.GetSection(ProjectRegistryOptions.SectionName));
builder.Services.Configure<ClaudeCodeOptions>(
    builder.Configuration.GetSection(ClaudeCodeOptions.SectionName));

// Core services and agent registry
builder.Services.AddRebelgent();

// SQLite persistence
builder.Services.AddRebelgentPersistence();

// Orchestration (project registry, workspace manager, task orchestrator)
builder.Services.AddRebelgentOrchestration();

// Claude Code process runner and agent runner
builder.Services.AddRebelgentClaudeCode();

// GitHub PR service (uses gh CLI)
builder.Services.AddRebelgentGitHub(builder.Configuration);

// Telegram bot (conditional on configuration)
builder.Services.AddRebelgentTelegram();

var app = builder.Build();

// Apply EF Core migrations at startup (development strategy; documented in architecture.md)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RebelgentDbContext>();
    var dataDir = Path.GetDirectoryName(db.Database.GetDbConnection().DataSource);
    if (!string.IsNullOrEmpty(dataDir))
        Directory.CreateDirectory(dataDir);
    await db.Database.MigrateAsync();
}

app.UseHttpsRedirection();

// --- Health ---
app.MapGet("/health", () => new HealthResponse("healthy", "Rebelgent"));

// --- System info ---
app.MapGet("/api/system/info", (IWebHostEnvironment env) =>
{
    var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    return new SystemInfoResponse("Rebelgent", version, env.EnvironmentName);
});

// --- Tasks ---
app.MapPost("/api/tasks", async (CreateTaskRequest req, ITaskService taskService, CancellationToken ct) =>
{
    try
    {
        var task = await taskService.CreateTaskAsync(
            new CreateTaskInput(req.ProjectId, req.Title, req.Description), ct);
        return Results.Created($"/api/tasks/{task.Id}", task.ToResponse());
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/tasks/{id:guid}", async (Guid id, ITaskService taskService, CancellationToken ct) =>
{
    var task = await taskService.GetTaskAsync(id, ct);
    return task is null ? Results.NotFound() : Results.Ok(task.ToResponse());
});

app.MapGet("/api/tasks", async (int count, ITaskService taskService, CancellationToken ct) =>
{
    var limit = Math.Clamp(count <= 0 ? 20 : count, 1, 100);
    var tasks = await taskService.GetRecentTasksAsync(limit, ct);
    var responses = tasks.Select(t => t.ToResponse()).ToList();
    return Results.Ok(new TaskListResponse(responses, responses.Count));
});

app.Run();

// Extension method placed here to keep Program.cs self-contained.
// The API project knows about both Core and Contracts, so mapping lives here.
static class AgentTaskExtensions
{
    public static TaskResponse ToResponse(this AgentTask task) => new(
        task.Id,
        task.ProjectId,
        task.Title,
        task.Description,
        task.AssignedRole.ToString(),
        task.Status.ToString(),
        task.Risk.ToString(),
        task.CreatedAt,
        task.StartedAt,
        task.CompletedAt,
        task.BranchName,
        task.PullRequestNumber
    );
}
