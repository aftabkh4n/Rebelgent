namespace Rebelgent.Contracts.Requests;

/// <summary>Request body for POST /api/tasks.</summary>
public record CreateTaskRequest(string ProjectId, string Title, string Description);
