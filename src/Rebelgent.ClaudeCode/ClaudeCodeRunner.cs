using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.Orchestration.Agents;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode;

/// <summary>
/// Invokes the Claude Code CLI in non-interactive mode to implement a coding task.
/// Uses SafeProcessRunner — no shell string injection, no cmd.exe intermediary.
/// The executable path comes from ClaudeCodeOptions; it is never derived from user input.
/// </summary>
internal sealed class ClaudeCodeRunner : ICodingAgentRunner
{
    private readonly IProcessRunner _processRunner;
    private readonly IOptions<ClaudeCodeOptions> _options;
    private readonly ILogger<ClaudeCodeRunner> _logger;

    public ClaudeCodeRunner(
        IProcessRunner processRunner,
        IOptions<ClaudeCodeOptions> options,
        ILogger<ClaudeCodeRunner> logger)
    {
        _processRunner = processRunner;
        _options = options;
        _logger = logger;
    }

    public async Task<AgentValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var path = _options.Value.ExecutablePath;

        if (string.IsNullOrWhiteSpace(path))
            return new AgentValidationResult { IsReady = false, ErrorMessage = "ClaudeCode:ExecutablePath is not configured. Set it via user-secrets or environment variable." };

        if (!File.Exists(path))
            return new AgentValidationResult { IsReady = false, ErrorMessage = $"Claude executable not found at '{path}'." };

        var versionResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = path,
            Arguments = ["--version"],
            WorkingDirectory = null,
            TimeoutMs = 10_000
        }, cancellationToken);

        if (!versionResult.Success)
            return new AgentValidationResult
            {
                IsReady = false,
                ErrorMessage = $"Claude --version failed (exit {versionResult.ExitCode}): {versionResult.StandardError.Trim()}"
            };

        _logger.LogInformation("Claude Code validated: {VersionOutput}", versionResult.StandardOutput.Trim());
        return new AgentValidationResult { IsReady = true };
    }

    public async Task<CodingAgentResult> RunAsync(CodingAgentRequest request, CancellationToken cancellationToken = default)
    {
        var executablePath = _options.Value.ExecutablePath;
        var prompt = ClaudePromptBuilder.Build(request);

        _logger.LogInformation("Invoking Claude Code for project {ProjectId} in {WorkspacePath}", request.ProjectId, request.WorkspacePath);

        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = executablePath,
            Arguments = ["-p", prompt, "--permission-mode", "bypassPermissions", "--output-format", "text"],
            WorkingDirectory = request.WorkspacePath,
            TimeoutMs = 15 * 60_000
        }, cancellationToken);

        if (result.TimedOut)
        {
            _logger.LogWarning("Claude Code timed out for project {ProjectId}", request.ProjectId);
            return new CodingAgentResult { Success = false, TimedOut = true, ErrorMessage = "Claude Code agent timed out." };
        }

        if (!result.Success)
        {
            _logger.LogWarning("Claude Code exited with code {ExitCode} for project {ProjectId}", result.ExitCode, request.ProjectId);
            return new CodingAgentResult
            {
                Success = false,
                Output = result.StandardOutput,
                ErrorMessage = $"Claude Code exited with code {result.ExitCode}: {result.StandardError}"
            };
        }

        return new CodingAgentResult { Success = true, Output = result.StandardOutput };
    }
}
