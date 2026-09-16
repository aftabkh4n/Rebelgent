using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Projects;

namespace Rebelgent.ClaudeCode.Improvement;

/// <inheritdoc cref="IImprovementOrchestrator"/>
public sealed class ImprovementOrchestrator : IImprovementOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IImprovementAnalystAgent _analystAgent;
    private readonly IProjectRegistry _projectRegistry;
    private readonly IOptions<SelfImprovementOptions> _selfImprovementOptions;
    private readonly ILogger<ImprovementOrchestrator> _logger;

    private enum PatternDisposition
    {
        Created,
        AnalystFailed,
        AnalystReturnedNoProposal,
        ParseFailed,
        EvaluationFailed,
        PersistenceFailed
    }

    private readonly record struct DraftResult(ImprovementProposal? Proposal, PatternDisposition Disposition);

    /// <summary>
    /// Internal test hook: override the evaluator used in <see cref="DraftAndEvaluateProposalAsync"/>.
    /// Must only be set in tests — never in production code paths.
    /// </summary>
    internal Func<string, ImprovementProposal, IReadOnlyList<ExecutionFailure>, EvaluationDataset>? EvaluatorOverride { get; set; }

    public ImprovementOrchestrator(
        IServiceScopeFactory scopeFactory,
        IImprovementAnalystAgent analystAgent,
        IProjectRegistry projectRegistry,
        IOptions<SelfImprovementOptions> selfImprovementOptions,
        ILogger<ImprovementOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _analystAgent = analystAgent;
        _projectRegistry = projectRegistry;
        _selfImprovementOptions = selfImprovementOptions;
        _logger = logger;
    }

    public async Task<ImprovementAnalyzeResult> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var projectId = _selfImprovementOptions.Value.ProjectId;
        if (string.IsNullOrWhiteSpace(projectId))
            return FailAnalyze("SelfImprovement:ProjectId is not configured. Set it to a registered project ID before running /improve analyze.");

        if (_projectRegistry.Find(projectId) is null)
            return FailAnalyze($"SelfImprovement:ProjectId '{projectId}' is not a registered project. Register it under Projects:Projects first.");

        IReadOnlyList<DetectedPattern> patterns;
        using (var scope = _scopeFactory.CreateScope())
        {
            var analysisService = scope.ServiceProvider.GetRequiredService<IExecutionAnalysisService>();
            patterns = await analysisService.AnalyzeAsync(projectId, cancellationToken: cancellationToken);
        }

        if (patterns.Count == 0)
        {
            return new ImprovementAnalyzeResult
            {
                Succeeded = true,
                Summary = $"Analysis complete for project '{projectId}'. No recurring failure patterns detected."
            };
        }

        var created = new List<ImprovementProposal>();
        var duplicates = 0;
        var analystFailures = 0;
        var analystNoProposals = 0;
        var parseFailures = 0;
        var evaluationFailures = 0;
        var persistenceFailures = 0;

        foreach (var pattern in patterns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ImprovementProposal? existing;
            using (var scope = _scopeFactory.CreateScope())
            {
                var proposalRepo = scope.ServiceProvider.GetRequiredService<IImprovementProposalRepository>();
                existing = await proposalRepo.GetActiveByFingerprintAsync(pattern.EvidenceFingerprint, cancellationToken);
            }

            if (existing is not null)
            {
                duplicates++;
                _logger.LogInformation(
                    "Skipping duplicate proposal for fingerprint {Fingerprint} — existing proposal {Id} is already {Status}",
                    pattern.EvidenceFingerprint, existing.Id, existing.Status);
                continue;
            }

            var draftResult = await DraftAndEvaluateProposalAsync(pattern, projectId, cancellationToken);
            switch (draftResult.Disposition)
            {
                case PatternDisposition.Created: created.Add(draftResult.Proposal!); break;
                case PatternDisposition.AnalystFailed: analystFailures++; break;
                case PatternDisposition.AnalystReturnedNoProposal: analystNoProposals++; break;
                case PatternDisposition.ParseFailed: parseFailures++; break;
                case PatternDisposition.EvaluationFailed: evaluationFailures++; break;
                case PatternDisposition.PersistenceFailed: persistenceFailures++; break;
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Analysis complete for project '{projectId}'.");
        sb.AppendLine($"Detected: {patterns.Count}");
        sb.AppendLine($"Created: {created.Count}");
        sb.Append($"Duplicates: {duplicates}");
        if (analystFailures > 0) sb.Append($"\nAnalyst failures: {analystFailures}");
        if (analystNoProposals > 0) sb.Append($"\nAnalyst returned no proposal: {analystNoProposals}");
        if (parseFailures > 0) sb.Append($"\nParse failures: {parseFailures}");
        if (evaluationFailures > 0) sb.Append($"\nEvaluation failures: {evaluationFailures}");
        if (persistenceFailures > 0) sb.Append($"\nPersistence failures: {persistenceFailures}");
        var summary = sb.ToString();

        return new ImprovementAnalyzeResult
        {
            Succeeded = true,
            PatternsDetected = patterns.Count,
            ProposalsCreated = created.Count,
            DuplicatesSkipped = duplicates,
            AnalystFailures = analystFailures,
            AnalystNoProposals = analystNoProposals,
            ParseFailures = parseFailures,
            EvaluationFailures = evaluationFailures,
            PersistenceFailures = persistenceFailures,
            NewProposals = created,
            Summary = summary
        };
    }

    private async Task<DraftResult> DraftAndEvaluateProposalAsync(DetectedPattern pattern, string projectId, CancellationToken cancellationToken)
    {
        var agentOutput = await _analystAgent.AnalyzeAsync(new ImprovementAnalysisInput
        {
            Title = pattern.Title,
            Category = pattern.Category.ToString(),
            Source = pattern.Source,
            Occurrences = pattern.Occurrences,
            Evidence = pattern.Evidence
        }, cancellationToken);

        if (!agentOutput.Succeeded)
        {
            _logger.LogError("Improvement Analyst agent failed for pattern '{Title}': {Error}", pattern.Title, agentOutput.ErrorMessage);
            var disposition = agentOutput.FailureKind switch
            {
                AnalystOutputKind.EmptyOutput => PatternDisposition.AnalystReturnedNoProposal,
                AnalystOutputKind.ParseFailed => PatternDisposition.ParseFailed,
                _ => PatternDisposition.AnalystFailed
            };
            return new DraftResult(null, disposition);
        }

        var riskLevel = Enum.TryParse<RiskLevel>(agentOutput.RiskLevel, ignoreCase: true, out var parsedRisk)
            ? parsedRisk
            : RiskLevel.Medium;

        var proposal = new ImprovementProposal(
            targetProjectId: projectId,
            title: agentOutput.ProposalTitle ?? pattern.Title,
            description: agentOutput.Description ?? pattern.Evidence,
            evidence: pattern.Evidence,
            targetArea: agentOutput.TargetArea ?? pattern.TargetArea,
            suggestedChange: agentOutput.SuggestedChange ?? "Review and address the recurring failure pattern described in the evidence.",
            riskLevel: riskLevel,
            evidenceFingerprint: pattern.EvidenceFingerprint);

        // Evaluate before AwaitingApproval. Purely data-driven — never executes code, never
        // touches a workspace, never modifies production behavior.
        EvaluationResult evaluationResult;
        try
        {
            List<ExecutionFailure> sampleFailures;
            using (var scope = _scopeFactory.CreateScope())
            {
                var failureRepo = scope.ServiceProvider.GetRequiredService<IExecutionFailureRepository>();
                var byCategory = await failureRepo.GetByCategoryAsync(pattern.Category, cancellationToken);
                sampleFailures = byCategory.Where(f => f.Source == pattern.Source).Take(5).ToList();
            }

            proposal.BeginEvaluation();
            var evaluator = EvaluatorOverride ?? RegressionEvaluator.BuildAndScore;
            var dataset = evaluator($"{pattern.Category}-{pattern.Source}", proposal, sampleFailures);
            evaluationResult = new EvaluationResult(
                proposal.Id, dataset.Name, dataset.Cases,
                $"{dataset.Cases.Count(c => c.Passed)}/{dataset.Cases.Count} regression cases passed.");
            proposal.CompleteEvaluation(
                $"{evaluationResult.PassedCases}/{evaluationResult.TotalCases} regression cases passed ({evaluationResult.PassRate:P0}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Evaluation failed for improvement proposal '{Title}'", proposal.Title);
            return new DraftResult(null, PatternDisposition.EvaluationFailed);
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var proposalRepo = scope.ServiceProvider.GetRequiredService<IImprovementProposalRepository>();
            var evalRepo = scope.ServiceProvider.GetRequiredService<IEvaluationResultRepository>();
            await proposalRepo.AddAsync(proposal, cancellationToken);
            await evalRepo.AddAsync(evaluationResult, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist improvement proposal '{Title}'", proposal.Title);
            return new DraftResult(null, PatternDisposition.PersistenceFailed);
        }

        _logger.LogInformation("Improvement proposal created: {Title} ({Status})", proposal.Title, proposal.Status);
        return new DraftResult(proposal, PatternDisposition.Created);
    }

    public async Task<ImprovementDecisionResult> ApproveAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        ImprovementProposal? proposal;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IImprovementProposalRepository>();
            proposal = await repo.GetByIdAsync(proposalId, cancellationToken);
        }

        if (proposal is null)
            return Fail("Improvement proposal not found.");

        if (proposal.Status == ImprovementProposalStatus.Approved)
        {
            var shortId = proposal.CreatedTaskId!.Value.ToString("N")[..8];
            return new ImprovementDecisionResult
            {
                Succeeded = true,
                Proposal = proposal,
                Summary = $"Proposal already approved. Task [{shortId}] was created for it — use /run {shortId} to start the normal Developer pipeline."
            };
        }

        if (proposal.Status == ImprovementProposalStatus.Rejected)
            return Fail("Cannot approve a proposal that has already been rejected.");

        if (proposal.Status != ImprovementProposalStatus.AwaitingApproval)
            return Fail($"Cannot approve a proposal in status '{proposal.Status}'. It must be AwaitingApproval.");

        // Fail safely and do NOT create a task if the target project is missing/unregistered —
        // never fall back to an arbitrary or placeholder project.
        if (_projectRegistry.Find(proposal.TargetProjectId) is null)
            return Fail($"Target project '{proposal.TargetProjectId}' is not registered. Cannot create a task. Register the project and try again.");

        AgentTask task;
        using (var scope = _scopeFactory.CreateScope())
        {
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
            var description =
                $"Self-improvement proposal: {proposal.Title}\n\n" +
                $"Target area: {proposal.TargetArea}\n" +
                $"Risk level: {proposal.RiskLevel}\n\n" +
                $"{proposal.Description}\n\n" +
                $"Suggested change:\n{proposal.SuggestedChange}\n\n" +
                $"Evidence:\n{proposal.Evidence}";

            var title = proposal.Title.Length > 100 ? proposal.Title[..100] : proposal.Title;
            task = await taskService.CreateTaskAsync(new CreateTaskInput(proposal.TargetProjectId, title, description), cancellationToken);
        }

        proposal.Approve(task.Id);

        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IImprovementProposalRepository>();
            await repo.UpdateAsync(proposal, cancellationToken);
        }

        var newShortId = task.Id.ToString("N")[..8];
        _logger.LogInformation("Improvement proposal {ProposalId} approved; created task {TaskId}", proposalId, task.Id);

        return new ImprovementDecisionResult
        {
            Succeeded = true,
            Proposal = proposal,
            Summary = $"Approved. Created task [{newShortId}] for this improvement.\n" +
                      "It has NOT been run automatically — this task goes through the normal " +
                      $"Developer → build/test → QA → Reviewer → PR → merge pipeline like any other task. " +
                      $"Use /run {newShortId} to start it."
        };
    }

    public async Task<ImprovementDecisionResult> RejectAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        ImprovementProposal? proposal;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IImprovementProposalRepository>();
            proposal = await repo.GetByIdAsync(proposalId, cancellationToken);
        }

        if (proposal is null)
            return Fail("Improvement proposal not found.");

        if (proposal.Status == ImprovementProposalStatus.Rejected)
            return new ImprovementDecisionResult { Succeeded = true, Proposal = proposal, Summary = $"Proposal '{proposal.Title}' already rejected." };

        if (proposal.Status == ImprovementProposalStatus.Approved)
            return Fail("Cannot reject a proposal that has already been approved.");

        if (proposal.Status is ImprovementProposalStatus.Implemented or ImprovementProposalStatus.Failed)
            return Fail($"Cannot reject a proposal in terminal status '{proposal.Status}'.");

        proposal.Reject();

        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IImprovementProposalRepository>();
            await repo.UpdateAsync(proposal, cancellationToken);
        }

        _logger.LogInformation("Improvement proposal {ProposalId} rejected", proposalId);
        return new ImprovementDecisionResult { Succeeded = true, Proposal = proposal, Summary = $"Proposal '{proposal.Title}' rejected." };
    }

    public async Task<ImprovementProposal?> GetProposalAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IImprovementProposalRepository>();
        return await repo.GetByIdAsync(proposalId, cancellationToken);
    }

    public async Task<IReadOnlyList<ImprovementProposal>> GetProposalsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IImprovementProposalRepository>();
        return await repo.GetAllAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionFailure>> GetRecentFailuresAsync(int count, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IExecutionFailureRepository>();
        var all = await repo.GetAllAsync(cancellationToken);
        return all.Take(count).ToList();
    }

    public async Task<AgentMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var calculator = scope.ServiceProvider.GetRequiredService<IMetricsCalculator>();
        return await calculator.ComputeAsync(cancellationToken);
    }

    private static ImprovementDecisionResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error,
        Summary = error
    };

    private static ImprovementAnalyzeResult FailAnalyze(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error,
        Summary = error
    };
}
