using Microsoft.Extensions.DependencyInjection;
using Rebelgent.ClaudeCode.Evolution;
using Rebelgent.ClaudeCode.Improvement;
using Rebelgent.ClaudeCode.Process;
using Rebelgent.ClaudeCode.QualityOrchestration;
using Rebelgent.ClaudeCode.ReleaseNotes;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Agents;
using Rebelgent.Orchestration.Orchestrator;
using Rebelgent.Orchestration.Process;

namespace Rebelgent.ClaudeCode.DependencyInjection;

/// <summary>Registers Claude Code process runner and agent runner.</summary>
public static class ClaudeCodeServiceExtensions
{
    public static IServiceCollection AddRebelgentClaudeCode(this IServiceCollection services)
    {
        services.AddSingleton<IProcessRunner, SafeProcessRunner>();
        services.AddSingleton<ICodingAgentRunner, ClaudeCodeRunner>();
        services.AddSingleton<IQualityOrchestrator, QualityOrchestrator>();
        services.AddSingleton<IReleaseNotesAgent, ClaudeCodeReleaseNotesAgent>();
        services.AddSingleton<IImprovementAnalystAgent, ClaudeCodeImprovementAnalystAgent>();
        services.AddSingleton<IImprovementOrchestrator, ImprovementOrchestrator>();
        services.AddSingleton<IAgentEvolutionManagerAgent, ClaudeCodeAgentEvolutionManagerAgent>();
        services.AddScoped<IAgentEvolutionOrchestrator, AgentEvolutionOrchestrator>();

        return services;
    }
}
