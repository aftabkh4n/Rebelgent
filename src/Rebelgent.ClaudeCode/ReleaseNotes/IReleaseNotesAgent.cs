namespace Rebelgent.ClaudeCode.ReleaseNotes;

public interface IReleaseNotesAgent
{
    Task<ReleaseNotesOutput> PrepareAsync(ReleaseNotesInput input, CancellationToken cancellationToken = default);
}
