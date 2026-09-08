using Microsoft.Extensions.DependencyInjection;

namespace Rebelgent.GitHub.DependencyInjection;

/// <summary>Registers GitHub PR services.</summary>
public static class GitHubServiceExtensions
{
    public static IServiceCollection AddRebelgentGitHub(this IServiceCollection services)
    {
        services.AddSingleton<IPullRequestService, GitHubCliPullRequestService>();
        services.AddSingleton<IPullRequestOrchestrator, PullRequestOrchestrator>();
        services.AddSingleton<IPullRequestMergeService, GitHubCliPullRequestMergeService>();
        services.AddSingleton<IMergeOrchestrator, MergeOrchestrator>();

        return services;
    }
}
