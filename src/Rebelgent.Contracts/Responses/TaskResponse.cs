namespace Rebelgent.Contracts.Responses;

/// <summary>Response contract representing a single agent task.</summary>
public record TaskResponse(
    Guid Id,
    string ProjectId,
    string Title,
    string Description,
    string AssignedRole,
    string Status,
    string Risk,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? BranchName,
    int? PullRequestNumber
);
