namespace Rebelgent.Contracts.Responses;

/// <summary>Response contract for a collection of tasks.</summary>
public record TaskListResponse(IReadOnlyList<TaskResponse> Tasks, int Total);
