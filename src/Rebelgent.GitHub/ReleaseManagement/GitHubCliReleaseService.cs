using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.GitHub.Release;

/// <summary>
/// Creates GitHub Releases using the gh CLI.
/// Never invokes cmd.exe, powershell.exe, or bash as intermediaries.
/// Never overwrites existing tags/releases. Never uses --force.
/// Creates releases only after explicit human approval.
/// </summary>
public sealed class GitHubCliReleaseService : IReleaseService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<GitHubCliReleaseService> _logger;

    public GitHubCliReleaseService(IProcessRunner processRunner, ILogger<GitHubCliReleaseService> logger)
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
            _logger.LogError("gh CLI not found: {Error}", versionResult.StandardError);
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

    public async Task<bool> TagExistsAsync(string tagName, string repositoryPath, string? gitHubRepository, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "release", "view", tagName };

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

        return result.Success;
    }

    public async Task<string?> GetLatestReleaseVersionAsync(string repositoryPath, string? gitHubRepository, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "release", "list", "--json", "tagName", "--limit", "50" };

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
            _logger.LogWarning("Failed to list GitHub releases for version context: {Error}", result.StandardError);
            return null;
        }

        return ParseLatestVersionFromJson(result.StandardOutput ?? string.Empty);
    }

    internal static string? ParseLatestVersionFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var tags = doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("tagName", out var p) ? p.GetString() : null);
            return SemverHelper.FindLatest(tags);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ReleaseCreatedResult> CreateReleaseAsync(CreateReleaseRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating GitHub release {Tag} targeting {Sha}", request.TagName, request.TargetCommitSha);

        var args = new List<string>
        {
            "release", "create", request.TagName,
            "--title", request.Title,
            "--notes", request.Notes,
            "--target", request.TargetCommitSha
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
            TimeoutMs = 30_000
        }, cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("gh release create failed for {Tag}: {Error}", request.TagName, result.StandardError);
            return ReleaseCreatedResult.Fail($"gh release create failed: {result.StandardError}");
        }

        // gh release create outputs the release URL on stdout
        var releaseUrl = result.StandardOutput?.Trim() ?? string.Empty;

        _logger.LogInformation("GitHub release created: {Url}", releaseUrl);
        return ReleaseCreatedResult.Ok(releaseUrl);
    }
}
