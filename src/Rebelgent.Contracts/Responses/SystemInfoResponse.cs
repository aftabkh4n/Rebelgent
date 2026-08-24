namespace Rebelgent.Contracts.Responses;

/// <summary>Response contract for the system information endpoint.</summary>
public record SystemInfoResponse(string ApplicationName, string Version, string Environment);
