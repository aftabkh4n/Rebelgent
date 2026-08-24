namespace Rebelgent.Contracts.Responses;

/// <summary>Response contract for the health endpoint.</summary>
public record HealthResponse(string Status, string Service);
