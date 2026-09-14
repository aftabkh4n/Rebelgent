using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rebelgent.Core.Publishing;
using Rebelgent.GitHub.Package;
using Rebelgent.GitHub.Release;

namespace Rebelgent.GitHub.DependencyInjection;

/// <summary>Registers GitHub PR, release, and package publishing services.</summary>
public static class GitHubServiceExtensions
{
    public static IServiceCollection AddRebelgentGitHub(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPullRequestService, GitHubCliPullRequestService>();
        services.AddSingleton<IPullRequestOrchestrator, PullRequestOrchestrator>();
        services.AddSingleton<IPullRequestMergeService, GitHubCliPullRequestMergeService>();
        services.AddSingleton<IMergeOrchestrator, MergeOrchestrator>();
        services.AddSingleton<IReleaseService, GitHubCliReleaseService>();
        services.AddSingleton<IReleaseOrchestrator, ReleaseOrchestrator>();
        services.AddSingleton<IPackagePublisher, NuGetPackagePublisher>();
        services.AddSingleton<IPackageOrchestrator, PackageOrchestrator>();
        services.Configure<NuGetOptions>(configuration.GetSection(NuGetOptions.SectionName));

        return services;
    }
}
