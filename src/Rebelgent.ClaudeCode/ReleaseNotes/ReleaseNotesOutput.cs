namespace Rebelgent.ClaudeCode.ReleaseNotes;

public sealed class ReleaseNotesOutput
{
    public bool Succeeded { get; init; }
    public string? Version { get; init; }
    public string? Title { get; init; }
    public string? Notes { get; init; }
    public bool HasBreakingChanges { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RawOutput { get; init; }
}
