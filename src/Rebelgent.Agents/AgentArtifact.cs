namespace Rebelgent.Agents;

/// <summary>
/// A named artifact produced by an agent during task execution.
/// Artifact types are intentionally kept provider-independent.
/// </summary>
public class AgentArtifact
{
    /// <summary>Logical type identifier, e.g. "source-code", "pull-request", "test-report".</summary>
    public string ArtifactType { get; }

    /// <summary>Human-readable name for the artifact.</summary>
    public string Name { get; }

    /// <summary>Optional URI or file path pointing to the artifact content.</summary>
    public string? Uri { get; }

    /// <summary>Optional inline content for small artifacts.</summary>
    public string? Content { get; }

    public AgentArtifact(string artifactType, string name, string? uri = null, string? content = null)
    {
        if (string.IsNullOrWhiteSpace(artifactType))
            throw new ArgumentException("Artifact type cannot be empty.", nameof(artifactType));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Artifact name cannot be empty.", nameof(name));

        ArtifactType = artifactType;
        Name = name;
        Uri = uri;
        Content = content;
    }
}
