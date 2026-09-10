namespace Rebelgent.ClaudeCode.ReleaseNotes;

public sealed class ReleaseNotesInput
{
    public required string TaskTitle { get; init; }
    public required string TaskDescription { get; init; }
    public required string MergeCommitSha { get; init; }
    public required string QaFindings { get; init; }
    public required string ReviewerFindings { get; init; }
    public string? PreviousVersion { get; init; }
    public string? ProjectId { get; init; }
}
