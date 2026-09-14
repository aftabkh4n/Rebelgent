using Microsoft.Extensions.DependencyInjection;
using Rebelgent.GitHub.Release;

namespace Rebelgent.GitHub.DependencyInjection;

/// <summary>Registers GitHub PR and release services.</summary>
public static class GitHubServiceExtensions
{
    public static IServiceCollection AddRebelgentGitHub(this IServiceCollection services)
    {
        services.AddSingleton<IPullRequestService, GitHubCliPullRequestService>();
        services.AddSingleton<IPullRequestOrchestrator, PullRequestOrchestrator>();
        services.AddSingleton<IPullRequestMergeService, GitHubCliPullRequestMergeService>();
        services.AddSingleton<IMergeOrchestrator, MergeOrchestrator>();
        services.AddSingleton<IReleaseService, GitHubCliReleaseService>();
        services.AddSingleton<IReleaseOrchestrator, ReleaseOrchestrator>();

        return services;
    }
}
