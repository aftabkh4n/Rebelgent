using Microsoft.Extensions.Logging;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.GitHub;

/// <summary>
/// Implements PR merge using the GitHub CLI (gh).
/// Never invokes cmd.exe, powershell.exe, or bash as intermediary shells.
/// Never uses --admin, --force, or auto-deletes branches.
/// Squash merge only for MVP.
/// </summary>
public sealed class GitHubCliPullRequestMergeService : IPullRequestMergeService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<GitHubCliPullRequestMergeService> _logger;

    public GitHubCliPullRequestMergeService(IProcessRunner processRunner, ILogger<GitHubCliPullRequestMergeService> logger)
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

    public async Task<PrStateResult> GetPrStateAsync(int pullRequestNumber, string repositoryPath, string? gitHubRepository, CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "pr", "view", pullRequestNumber.ToString(),
            "--json", "headRefName,baseRefName,state,mergeable"
        };

        if (!string.IsNullOrWhiteSpace(gitHubRepository))
        {
            args.Add("--repo");
            args.Add(gitHubRepository);
        }

        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "gh",
            Arguments = args,
            WorkingDirectory = repositoryPath,
            TimeoutMs = 15_000
        }, cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("gh pr view failed for PR #{Number}: {Error}", pullRequestNumber, result.StandardError);
            return PrStateResult.Fail($"Could not retrieve PR state: {result.StandardError}");
        }

        var json = result.StandardOutput?.Trim() ?? string.Empty;
        return ParsePrState(json, pullRequestNumber);
    }

    public async Task<PullRequestMergeResult> MergeAsync(MergeRequest request, CancellationToken cancellationToken = default)
    {
        const string mergeMethod = "squash";

        _logger.LogInformation("Merging PR #{Number} via {Method}", request.PullRequestNumber, mergeMethod);

        var args = new List<string>
        {
            "pr", "merge", request.PullRequestNumber.ToString(),
            "--squash"
        };

        if (!string.IsNullOrWhiteSpace(request.GitHubRepository))
        {
            args.Add("--repo");
            args.Add(request.GitHubRepository);
        }

        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "gh",
            Arguments = args,
            WorkingDirectory = request.RepositoryPath,
            TimeoutMs = 60_000
        }, cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("gh pr merge failed for PR #{Number}: {Error}", request.PullRequestNumber, result.StandardError);
            return PullRequestMergeResult.Fail($"gh pr merge failed: {result.StandardError}");
        }

        // gh pr merge does not return the commit SHA in a machine-readable way — retrieve it from the PR
        var commitSha = await GetMergeCommitShaAsync(request, cancellationToken);

        _logger.LogInformation("PR #{Number} merged via {Method} at {Sha}", request.PullRequestNumber, mergeMethod, commitSha);

        return PullRequestMergeResult.Ok(commitSha, mergeMethod);
    }

    private async Task<string> GetMergeCommitShaAsync(MergeRequest request, CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "pr", "view", request.PullRequestNumber.ToString(),
            "--json", "mergeCommit"
        };

        if (!string.IsNullOrWhiteSpace(request.GitHubRepository))
        {
            args.Add("--repo");
            args.Add(request.GitHubRepository);
        }

        var result = await _processRunner.RunAsync(new ProcessRunOptions
        {
            FileName = "gh",
            Arguments = args,
            WorkingDirectory = request.RepositoryPath,
            TimeoutMs = 15_000
        }, cancellationToken);

        if (!result.Success)
            return string.Empty;

        return ParseMergeCommitSha(result.StandardOutput?.Trim() ?? string.Empty);
    }

    private static PrStateResult ParsePrState(string json, int prNumber)
    {
        // Minimal JSON parsing without a library dependency.
        // Expected format: {"headRefName":"branch","baseRefName":"main","state":"OPEN","mergeable":"MERGEABLE"}
        if (string.IsNullOrWhiteSpace(json))
            return PrStateResult.Fail($"Empty response from gh pr view #{prNumber}");

        var headBranch = ExtractJsonStringValue(json, "headRefName");
        var baseBranch = ExtractJsonStringValue(json, "baseRefName");
        var state = ExtractJsonStringValue(json, "state");
        var mergeableStr = ExtractJsonStringValue(json, "mergeable");

        bool? mergeable = mergeableStr switch
        {
            "MERGEABLE" => true,
            "CONFLICTING" => false,
            _ => null
        };

        return PrStateResult.Ok(headBranch ?? string.Empty, baseBranch ?? string.Empty, state ?? string.Empty, mergeable);
    }

    private static string ParseMergeCommitSha(string json)
    {
        // Expected: {"mergeCommit":{"oid":"abc123..."}}
        var oid = ExtractJsonStringValue(json, "oid");
        return oid ?? string.Empty;
    }

    private static string? ExtractJsonStringValue(string json, string key)
    {
        var search = $"\"{key}\":\"";
        var start = json.IndexOf(search, StringComparison.Ordinal);
        if (start < 0) return null;
        start += search.Length;
        var end = json.IndexOf('"', start);
        if (end < 0) return null;
        return json[start..end];
    }
}
