using Microsoft.Extensions.Logging;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.GitHub;

/// <summary>
/// Implements pull request creation using the GitHub CLI (gh).
/// Never invokes cmd.exe, powershell.exe, or bash as intermediary shells.
/// Never force-pushes. Never pushes to main/master.
/// All repository and remote values come from registered project configuration.
/// </summary>
public sealed class GitHubCliPullRequestService : IPullRequestService
{
    private static readonly HashSet<string> ProtectedBranches =
        new(StringComparer.OrdinalIgnoreCase) { "main", "master", "develop", "release" };

    private readonly IProcessRunner _processRunner;
    private readonly ILogger<GitHubCliPullRequestService> _logger;

    public GitHubCliPullRequestService(IProcessRunner processRunner, ILogger<GitHubCliPullRequestService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<GitHubValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var versionResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "gh",
            Arguments = ["--version"],
            TimeoutMs = 10_000
        }, cancellationToken);

        if (!versionResult.Success)
        {
            _logger.LogError("gh CLI not found or failed: {Error}", versionResult.StandardError);
            return GitHubValidationResult.Unavailable("GitHub CLI (gh) is not available. Install it from https://cli.github.com/");
        }

        var authResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "gh",
            Arguments = ["auth", "status"],
            TimeoutMs = 10_000
        }, cancellationToken);

        if (!authResult.Success)
        {
            _logger.LogError("gh not authenticated: {Error}", authResult.StandardError);
            return GitHubValidationResult.Unavailable("GitHub CLI is not authenticated. Run: gh auth login");
        }

        return GitHubValidationResult.Ready();
    }

    public async Task<PullRequestCreatedResult> PushAndCreateAsync(PushAndCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (ProtectedBranches.Contains(request.BranchName))
        {
            _logger.LogError("Refused to push protected branch: {Branch}", request.BranchName);
            return Fail($"Refused to push protected branch '{request.BranchName}'. Agent branches must not be named like protected branches.");
        }

        _logger.LogInformation("Pushing branch {Branch} to {Remote}", request.BranchName, request.RemoteName);

        var pushResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "git",
            Arguments = ["push", request.RemoteName, request.BranchName],
            WorkingDirectory = request.RepositoryPath,
            TimeoutMs = 60_000
        }, cancellationToken);

        if (!pushResult.Success)
        {
            _logger.LogError("git push failed: {Error}", pushResult.StandardError);
            return Fail($"git push failed: {pushResult.StandardError}");
        }

        _logger.LogInformation("Creating pull request: {Title}", request.Title);

        var prArgs = new List<string>
        {
            "pr", "create",
            "--title", request.Title,
            "--body", request.Body,
            "--base", request.BaseBranch,
            "--head", request.BranchName
        };

        if (!string.IsNullOrWhiteSpace(request.GitHubRepository))
        {
            prArgs.Add("--repo");
            prArgs.Add(request.GitHubRepository);
        }

        var prResult = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "gh",
            Arguments = prArgs,
            WorkingDirectory = request.RepositoryPath,
            TimeoutMs = 30_000
        }, cancellationToken);

        if (!prResult.Success)
        {
            _logger.LogError("gh pr create failed: {Error}", prResult.StandardError);
            return Fail($"gh pr create failed: {prResult.StandardError}");
        }

        var prUrl = prResult.StandardOutput?.Trim() ?? string.Empty;
        var prNumber = ParsePrNumber(prUrl);

        _logger.LogInformation("Pull request created: #{Number} {Url}", prNumber, prUrl);

        return new PullRequestCreatedResult
        {
            Succeeded = true,
            PullRequestNumber = prNumber,
            PullRequestUrl = prUrl
        };
    }

    private static int? ParsePrNumber(string prUrl)
    {
        // gh pr create outputs the URL, e.g. https://github.com/owner/repo/pull/42
        var lastSlash = prUrl.LastIndexOf('/');
        if (lastSlash >= 0 && int.TryParse(prUrl.AsSpan(lastSlash + 1), out var number) && number > 0)
            return number;
        return null;
    }

    private static PullRequestCreatedResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error
    };
}
