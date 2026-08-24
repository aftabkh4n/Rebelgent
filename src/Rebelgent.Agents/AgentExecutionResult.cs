using Rebelgent.Core.Domain;

namespace Rebelgent.Agents;

/// <summary>The outcome of an agent execution, including any produced artifacts and a recommended next status.</summary>
public class AgentExecutionResult
{
    public bool Success { get; }
    public string Summary { get; }
    public string? Error { get; }
    public IReadOnlyList<AgentArtifact> Artifacts { get; }
    public AgentTaskStatus? RecommendedNextStatus { get; }

    private AgentExecutionResult(
        bool success,
        string summary,
        string? error,
        IReadOnlyList<AgentArtifact> artifacts,
        AgentTaskStatus? recommendedNextStatus)
    {
        Summary = summary;
        Success = success;
        Error = error;
        Artifacts = artifacts;
        RecommendedNextStatus = recommendedNextStatus;
    }

    public static AgentExecutionResult Succeeded(
        string summary,
        AgentTaskStatus? recommendedNextStatus = null,
        IEnumerable<AgentArtifact>? artifacts = null)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("Summary cannot be empty.", nameof(summary));

        return new AgentExecutionResult(
            true,
            summary,
            null,
            (artifacts ?? []).ToList().AsReadOnly(),
            recommendedNextStatus);
    }

    public static AgentExecutionResult Failed(
        string summary,
        string error,
        AgentTaskStatus? recommendedNextStatus = null)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("Summary cannot be empty.", nameof(summary));
        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("Error cannot be empty.", nameof(error));

        return new AgentExecutionResult(
            false,
            summary,
            error,
            [],
            recommendedNextStatus);
    }
}
