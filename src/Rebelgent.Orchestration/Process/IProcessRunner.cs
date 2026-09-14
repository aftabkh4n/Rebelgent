namespace Rebelgent.Orchestration.Process;

/// <summary>Runs external processes safely without shell string injection.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken = default);
}
