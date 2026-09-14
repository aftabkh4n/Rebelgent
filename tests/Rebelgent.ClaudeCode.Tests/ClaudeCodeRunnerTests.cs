using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rebelgent.ClaudeCode.Options;
using Rebelgent.Orchestration.Agents;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode.Tests;

public class ClaudeCodeRunnerTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new();

        public List<ProcessRunOptions> Calls { get; } = [];

        public void Enqueue(ProcessResult result) => _results.Enqueue(result);

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken = default)
        {
            Calls.Add(options);
            var result = _results.Count > 0
                ? _results.Dequeue()
                : new ProcessResult { Success = true, ExitCode = 0 };
            return Task.FromResult(result);
        }
    }

    private static ClaudeCodeRunner BuildRunner(FakeProcessRunner fake, string executablePath)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ClaudeCodeOptions { ExecutablePath = executablePath });
        return new ClaudeCodeRunner(fake, options, NullLogger<ClaudeCodeRunner>.Instance);
    }

    // ── ValidateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_EmptyExecutablePath_ReturnsNotReady()
    {
        var fake = new FakeProcessRunner();
        var runner = BuildRunner(fake, string.Empty);

        var result = await runner.ValidateAsync();

        Assert.False(result.IsReady);
        Assert.Contains("ExecutablePath", result.ErrorMessage);
        Assert.Empty(fake.Calls); // no process should be launched
    }

    [Fact]
    public async Task ValidateAsync_WhitespaceExecutablePath_ReturnsNotReady()
    {
        var fake = new FakeProcessRunner();
        var runner = BuildRunner(fake, "   ");

        var result = await runner.ValidateAsync();

        Assert.False(result.IsReady);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task ValidateAsync_PathDoesNotExist_ReturnsNotReady()
    {
        var fake = new FakeProcessRunner();
        // Use a path that definitely does not exist
        var runner = BuildRunner(fake, @"C:\does-not-exist\claude.exe");

        var result = await runner.ValidateAsync();

        Assert.False(result.IsReady);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.Calls); // file-not-found short-circuits before process launch
    }

    [Fact]
    public async Task ValidateAsync_VersionCheckFails_ReturnsNotReady()
    {
        // Point at a real file that exists (we use a temp file as a stand-in for the executable)
        var tempFile = Path.GetTempFileName();
        try
        {
            var fake = new FakeProcessRunner();
            fake.Enqueue(new ProcessResult { Success = false, ExitCode = 1, StandardError = "not a claude binary" });
            var runner = BuildRunner(fake, tempFile);

            var result = await runner.ValidateAsync();

            Assert.False(result.IsReady);
            Assert.Contains("--version", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            // Exactly one process call: --version
            Assert.Single(fake.Calls);
            Assert.Equal(tempFile, fake.Calls[0].FileName);
            Assert.Contains("--version", fake.Calls[0].Arguments);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ValidateAsync_VersionCheckSucceeds_ReturnsReady()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var fake = new FakeProcessRunner();
            fake.Enqueue(new ProcessResult { Success = true, ExitCode = 0, StandardOutput = "claude 2.1.139" });
            var runner = BuildRunner(fake, tempFile);

            var result = await runner.ValidateAsync();

            Assert.True(result.IsReady);
            Assert.Null(result.ErrorMessage);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ValidateAsync_UsesConfiguredPathAsFileName()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var fake = new FakeProcessRunner();
            fake.Enqueue(new ProcessResult { Success = true, ExitCode = 0 });
            var runner = BuildRunner(fake, tempFile);

            await runner.ValidateAsync();

            Assert.Single(fake.Calls);
            Assert.Equal(tempFile, fake.Calls[0].FileName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── RunAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_UsesConfiguredPathAsFileName()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var fake = new FakeProcessRunner();
            fake.Enqueue(new ProcessResult { Success = true, ExitCode = 0, StandardOutput = "done" });
            var runner = BuildRunner(fake, tempFile);

            await runner.RunAsync(new CodingAgentRequest
            {
                TaskDescription = "Add a method",
                WorkspacePath = Path.GetTempPath(),
                ProjectId = "sandbox"
            });

            Assert.Single(fake.Calls);
            Assert.Equal(tempFile, fake.Calls[0].FileName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunAsync_PassesBypassPermissionsFlag()
    {
        var fake = new FakeProcessRunner();
        fake.Enqueue(new ProcessResult { Success = true, ExitCode = 0, StandardOutput = "done" });
        var runner = BuildRunner(fake, @"C:\any\claude.exe");

        await runner.RunAsync(new CodingAgentRequest
        {
            TaskDescription = "task",
            WorkspacePath = Path.GetTempPath(),
            ProjectId = "sandbox"
        });

        var args = fake.Calls[0].Arguments;
        Assert.Contains("--permission-mode", args);
        Assert.Contains("bypassPermissions", args);
        Assert.Contains("--output-format", args);
        Assert.Contains("text", args);
    }

    [Fact]
    public async Task RunAsync_NeverUsesShellExecute()
    {
        // SafeProcessRunner always sets UseShellExecute=false; this test confirms
        // RunAsync never passes cmd/powershell/bash as FileName.
        var fake = new FakeProcessRunner();
        fake.Enqueue(new ProcessResult { Success = true, ExitCode = 0 });
        var runner = BuildRunner(fake, @"C:\any\claude.exe");

        await runner.RunAsync(new CodingAgentRequest
        {
            TaskDescription = "task",
            WorkspacePath = Path.GetTempPath(),
            ProjectId = "sandbox"
        });

        var fileName = fake.Calls[0].FileName;
        Assert.DoesNotContain("cmd", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bash", fileName, StringComparison.OrdinalIgnoreCase);
    }
}
