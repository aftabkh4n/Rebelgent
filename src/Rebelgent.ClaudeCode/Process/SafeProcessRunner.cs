using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode.Process;

/// <summary>
/// Runs external processes using ProcessStartInfo with ArgumentList (no shell string injection).
/// Never invokes cmd.exe, powershell.exe, or bash as intermediary shells.
/// </summary>
internal sealed class SafeProcessRunner : IProcessRunner
{
    private readonly ILogger<SafeProcessRunner> _logger;

    public SafeProcessRunner(ILogger<SafeProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = options.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in options.Arguments)
            psi.ArgumentList.Add(arg);

        if (options.WorkingDirectory is not null)
            psi.WorkingDirectory = options.WorkingDirectory;

        _logger.LogDebug("Running: {FileName} {Args}", options.FileName, string.Join(" ", options.Arguments));

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        using var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdoutCompletion = new TaskCompletionSource<bool>();
        var stderrCompletion = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) stdoutCompletion.TrySetResult(true);
            else stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) stderrCompletion.TrySetResult(true);
            else stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.TimeoutMs);

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            await Task.WhenAll(stdoutCompletion.Task, stderrCompletion.Task);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }
            if (!timedOut)
                throw;
        }

        var exitCode = timedOut ? -1 : process.ExitCode;

        _logger.LogDebug("Process exited: {ExitCode} (timedOut={TimedOut})", exitCode, timedOut);

        return new ProcessResult
        {
            Success = exitCode == 0 && !timedOut,
            ExitCode = exitCode,
            StandardOutput = stdoutBuilder.ToString(),
            StandardError = stderrBuilder.ToString(),
            TimedOut = timedOut
        };
    }
}
