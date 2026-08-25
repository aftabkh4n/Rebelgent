namespace Rebelgent.Core.Services;

/// <summary>Input for creating a new task through <see cref="ITaskService"/>.</summary>
public record CreateTaskInput(string ProjectId, string Title, string Description);
