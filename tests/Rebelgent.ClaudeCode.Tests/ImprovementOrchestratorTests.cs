using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebelgent.ClaudeCode.Improvement;
using Rebelgent.Core.Domain;
using Rebelgent.Core.Repositories;
using Rebelgent.Core.Services;
using Rebelgent.Orchestration.Process;
using Rebelgent.Orchestration.Projects;
using Rebelgent.Orchestration.Workspace;

namespace Rebelgent.ClaudeCode.Tests;

public class ImprovementOrchestratorTests
{
    private static readonly ProjectDefinition SandboxProject = new()
    {
        Id = "sandbox",
        Name = "Sandbox",
        RepositoryPath = @"D:\Projects\RebelgentSandbox",
        DefaultBranch = "main"
    };

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeExecutionAnalysisService : IExecutionAnalysisService
    {
        public List<DetectedPattern> Patterns { get; set; } = [];
        public int CategorizeCallCount { get; private set; }
        public int AnalyzeCallCount { get; private set; }
        public string? LastProjectId { get; private set; }

        public Task<int> CategorizeAndPersistFailuresAsync(string projectId, CancellationToken ct = default)
        {
            CategorizeCallCount++;
            LastProjectId = projectId;
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<DetectedPattern>> AnalyzeAsync(string projectId, int minOccurrences = 2, CancellationToken ct = default)
        {
            AnalyzeCallCount++;
            LastProjectId = projectId;
            return Task.FromResult<IReadOnlyList<DetectedPattern>>(Patterns);
        }
    }

    private sealed class FakeImprovementAnalystAgent : IImprovementAnalystAgent
    {
        public ImprovementAnalysisOutput Output { get; set; } = new()
        {
            Succeeded = true,
            ProposalTitle = "Add build reminder to Developer prompt",
            TargetArea = "Developer Prompt",
            RiskLevel = "Low",
            SuggestedChange = "Add an explicit build-before-commit reminder.",
            Description = "The Developer agent has repeatedly failed the build for the same reason."
        };

        public int CallCount { get; private set; }

        public Task<ImprovementAnalysisOutput> AnalyzeAsync(ImprovementAnalysisInput input, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(Output);
        }
    }

    private sealed class FakeProposalRepository : IImprovementProposalRepository
    {
        public List<ImprovementProposal> Proposals { get; } = [];
        public int AddCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public bool ThrowOnAdd { get; set; }

        public Task AddAsync(ImprovementProposal proposal, CancellationToken ct = default)
        {
            if (ThrowOnAdd) throw new InvalidOperationException("Simulated persistence failure.");
            AddCallCount++;
            Proposals.Add(proposal);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ImprovementProposal proposal, CancellationToken ct = default)
        {
            UpdateCallCount++;
            return Task.CompletedTask;
        }

        public Task<ImprovementProposal?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Proposals.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<ImprovementProposal>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ImprovementProposal>>(Proposals);

        public Task<ImprovementProposal?> GetActiveByFingerprintAsync(string evidenceFingerprint, CancellationToken ct = default) =>
            Task.FromResult(Proposals.FirstOrDefault(p =>
                p.EvidenceFingerprint == evidenceFingerprint &&
                p.Status is not (ImprovementProposalStatus.Rejected or ImprovementProposalStatus.Implemented or ImprovementProposalStatus.Failed)));
    }

    private sealed class FakeEvaluationResultRepository : IEvaluationResultRepository
    {
        public List<EvaluationResult> Results { get; } = [];

        public Task AddAsync(EvaluationResult result, CancellationToken ct = default)
        {
            Results.Add(result);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EvaluationResult>> GetByProposalIdAsync(Guid proposalId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EvaluationResult>>(Results.Where(r => r.ProposalId == proposalId).ToList());
    }

    private sealed class FakeExecutionFailureRepository : IExecutionFailureRepository
    {
        public List<ExecutionFailure> Failures { get; set; } = [];

        public Task AddAsync(ExecutionFailure failure, CancellationToken ct = default)
        {
            Failures.Add(failure);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExecutionFailure>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionFailure>>(Failures);

        public Task<IReadOnlyList<ExecutionFailure>> GetByCategoryAsync(FailureCategory category, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionFailure>>(Failures.Where(f => f.Category == category).ToList());

        public Task<bool> ExistsForExecutionAsync(Guid executionId, CancellationToken ct = default) =>
            Task.FromResult(Failures.Any(f => f.ExecutionId == executionId));
    }

    private sealed class FakeTaskService : ITaskService
    {
        public AgentTask? CreatedTask { get; set; }
        public int CreateTaskCallCount { get; private set; }
        public CreateTaskInput? LastInput { get; private set; }
        public bool ThrowOnCreate { get; set; }

        public Task<AgentTask> CreateTaskAsync(CreateTaskInput input, CancellationToken ct = default)
        {
            CreateTaskCallCount++;
            LastInput = input;
            if (ThrowOnCreate)
                throw new InvalidOperationException("Simulated task creation failure.");
            CreatedTask ??= new AgentTask(input.ProjectId, input.Title, input.Description, AgentRole.EngineeringManager);
            return Task.FromResult(CreatedTask);
        }

        public Task<AgentTask?> GetTaskAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<AgentTask>> GetRecentTasksAsync(int n = 20, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<AgentTask>> FindByPrefixAsync(string p, int max, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AgentTask?> TransitionAsync(Guid id, AgentTaskStatus s, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AgentTask?> SetBranchNameAsync(Guid id, string branch, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AgentTask?> SetPullRequestInfoAsync(Guid id, int number, string url, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AgentTask?> SetMergeInfoAsync(Guid id, string sha, string method, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeMetricsCalculator : IMetricsCalculator
    {
        public AgentMetricsSnapshot Snapshot { get; set; } = new(
            totalTasks: 0, taskSuccessRate: 0, developerFailureRate: 0, qaPassRate: 0,
            reviewApprovalRate: 0, averageRetriesPerTask: 0, releaseFailureRate: 0,
            packageFailureRate: 0, failuresByCategory: new Dictionary<FailureCategory, int>());

        public Task<AgentMetricsSnapshot> ComputeAsync(CancellationToken ct = default) => Task.FromResult(Snapshot);
    }

    private sealed class FakeProjectRegistry : IProjectRegistry
    {
        private readonly List<ProjectDefinition> _projects;
        public FakeProjectRegistry(params ProjectDefinition[] projects) => _projects = projects.ToList();
        public ProjectDefinition? Find(string projectId) => _projects.FirstOrDefault(p => p.Id == projectId);
        public IReadOnlyCollection<ProjectDefinition> GetAll() => _projects;
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services;

        public FakeServiceProvider(Dictionary<Type, object> services) => _services = services;

        public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;
        public FakeScopeFactory(IServiceProvider provider) => _provider = provider;

        public IServiceScope CreateScope() => new FakeScope(_provider);

        private sealed class FakeScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; }
            public FakeScope(IServiceProvider p) => ServiceProvider = p;
            public void Dispose() { }
        }
    }

    private sealed record Fixture(
        ImprovementOrchestrator Orchestrator,
        FakeExecutionAnalysisService AnalysisService,
        FakeImprovementAnalystAgent AnalystAgent,
        FakeProposalRepository ProposalRepository,
        FakeEvaluationResultRepository EvaluationRepository,
        FakeExecutionFailureRepository FailureRepository,
        FakeTaskService TaskService,
        FakeMetricsCalculator MetricsCalculator,
        FakeProjectRegistry ProjectRegistry);

    /// <param name="selfImprovementProjectId">Value for SelfImprovement:ProjectId. Null simulates
    /// it being unconfigured.</param>
    /// <param name="registeredProjects">Projects known to the registry. Defaults to just
    /// "sandbox" (which also matches the default self-improvement project ID) so most tests
    /// exercise the happy path without extra setup.</param>
    private static Fixture Build(string? selfImprovementProjectId = "sandbox", ProjectDefinition[]? registeredProjects = null)
    {
        var analysisService = new FakeExecutionAnalysisService();
        var analystAgent = new FakeImprovementAnalystAgent();
        var proposalRepository = new FakeProposalRepository();
        var evaluationRepository = new FakeEvaluationResultRepository();
        var failureRepository = new FakeExecutionFailureRepository();
        var taskService = new FakeTaskService();
        var metricsCalculator = new FakeMetricsCalculator();
        var projectRegistry = new FakeProjectRegistry(registeredProjects ?? [SandboxProject]);
        var options = Microsoft.Extensions.Options.Options.Create(new SelfImprovementOptions { ProjectId = selfImprovementProjectId });

        var provider = new FakeServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IExecutionAnalysisService)] = analysisService,
            [typeof(IImprovementProposalRepository)] = proposalRepository,
            [typeof(IEvaluationResultRepository)] = evaluationRepository,
            [typeof(IExecutionFailureRepository)] = failureRepository,
            [typeof(ITaskService)] = taskService,
            [typeof(IMetricsCalculator)] = metricsCalculator
        });
        var scopeFactory = new FakeScopeFactory(provider);

        var orchestrator = new ImprovementOrchestrator(scopeFactory, analystAgent, projectRegistry, options, NullLogger<ImprovementOrchestrator>.Instance);

        return new Fixture(orchestrator, analysisService, analystAgent, proposalRepository, evaluationRepository, failureRepository, taskService, metricsCalculator, projectRegistry);
    }

    private static DetectedPattern MakePattern(string fingerprint = "FP-1") => new(
        Title: "Developer failed the build 3 times",
        Evidence: "3 recent build failures.",
        Category: FailureCategory.BuildFailure,
        Source: "BackendDeveloper",
        TargetArea: "Developer Prompt / Workflow",
        Occurrences: 3,
        EvidenceFingerprint: fingerprint);

    // ── AnalyzeAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_NoPatterns_ReturnsSuccessWithZeroCounts()
    {
        var fixture = Build();

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.PatternsDetected);
        Assert.Equal(0, result.ProposalsCreated);
    }

    [Fact]
    public async Task AnalyzeAsync_SelfImprovementProjectIdNotConfigured_FailsSafelyWithoutAnalyzing()
    {
        var fixture = Build(selfImprovementProjectId: null);

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("not configured", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.AnalysisService.AnalyzeCallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_SelfImprovementProjectNotRegistered_FailsSafelyWithoutAnalyzing()
    {
        var fixture = Build(selfImprovementProjectId: "rebelgent"); // not in the registry (no projects passed)

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("not a registered project", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.AnalysisService.AnalyzeCallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_AnalyzesTheConfiguredProject()
    {
        var fixture = Build(selfImprovementProjectId: "sandbox");

        await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal("sandbox", fixture.AnalysisService.LastProjectId);
    }

    [Fact]
    public async Task AnalyzeAsync_NewPattern_CreatesProposalAwaitingApproval()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(1, result.ProposalsCreated);
        var proposal = Assert.Single(fixture.ProposalRepository.Proposals);
        Assert.Equal(ImprovementProposalStatus.AwaitingApproval, proposal.Status);
        Assert.NotNull(proposal.EvaluationSummary);
    }

    [Fact]
    public async Task AnalyzeAsync_NewPattern_ProposalTargetsConfiguredProject()
    {
        var fixture = Build(selfImprovementProjectId: "sandbox");
        fixture.AnalysisService.Patterns = [MakePattern()];

        await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal("sandbox", fixture.ProposalRepository.Proposals.Single().TargetProjectId);
    }

    [Fact]
    public async Task AnalyzeAsync_NewPattern_PersistsEvaluationResult()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];

        await fixture.Orchestrator.AnalyzeAsync();

        Assert.Single(fixture.EvaluationRepository.Results);
    }

    [Fact]
    public async Task AnalyzeAsync_ExistingActiveProposalForFingerprint_SkipsDuplicate()
    {
        var fixture = Build();
        var pattern = MakePattern("FP-DUP");
        fixture.AnalysisService.Patterns = [pattern];

        var existing = new ImprovementProposal("sandbox", "Existing", "desc", "evidence", "area", "change", RiskLevel.Low, "FP-DUP");
        fixture.ProposalRepository.Proposals.Add(existing);

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(1, result.DuplicatesSkipped);
        Assert.Equal(0, result.ProposalsCreated);
        Assert.Equal(0, fixture.AnalystAgent.CallCount);
        Assert.Single(fixture.ProposalRepository.Proposals); // still just the pre-existing one
    }

    [Fact]
    public async Task AnalyzeAsync_RejectedProposalForFingerprint_AllowsReproposal()
    {
        var fixture = Build();
        var pattern = MakePattern("FP-REJECTED");
        fixture.AnalysisService.Patterns = [pattern];

        var rejected = new ImprovementProposal("sandbox", "Old", "desc", "evidence", "area", "change", RiskLevel.Low, "FP-REJECTED");
        rejected.BeginEvaluation();
        rejected.CompleteEvaluation("summary");
        rejected.Reject();
        fixture.ProposalRepository.Proposals.Add(rejected);

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(1, result.ProposalsCreated);
        Assert.Equal(0, result.DuplicatesSkipped);
    }

    [Fact]
    public async Task AnalyzeAsync_AnalystAgentFails_SkipsPatternWithoutCreatingProposal()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];
        fixture.AnalystAgent.Output = new ImprovementAnalysisOutput { Succeeded = false, ErrorMessage = "agent unavailable" };

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(0, result.ProposalsCreated);
        Assert.Empty(fixture.ProposalRepository.Proposals);
    }

    [Fact]
    public async Task AnalyzeAsync_HappyPath_Detected1_Created1_AllCountersZeroExceptCreated()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];
        // Default analyst output is a successful proposal

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(1, result.PatternsDetected);
        Assert.Equal(1, result.ProposalsCreated);
        Assert.Equal(0, result.AnalystFailures);
        Assert.Equal(0, result.AnalystNoProposals);
        Assert.Equal(0, result.ParseFailures);
        Assert.Equal(0, result.EvaluationFailures);
        Assert.Equal(0, result.PersistenceFailures);
    }

    [Fact]
    public async Task AnalyzeAsync_AnalystFails_ProcessError_Reports_AnalystFailures()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];
        fixture.AnalystAgent.Output = new ImprovementAnalysisOutput
        {
            Succeeded = false,
            FailureKind = AnalystOutputKind.ProcessFailed,
            ErrorMessage = "process error"
        };

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(0, result.ProposalsCreated);
        Assert.Equal(1, result.AnalystFailures);
        Assert.Equal(0, result.ParseFailures);
    }

    [Fact]
    public async Task AnalyzeAsync_AnalystFails_EmptyOutput_Reports_AnalystNoProposals()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];
        fixture.AnalystAgent.Output = new ImprovementAnalysisOutput
        {
            Succeeded = false,
            FailureKind = AnalystOutputKind.EmptyOutput,
            ErrorMessage = "no output"
        };

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(0, result.ProposalsCreated);
        Assert.Equal(1, result.AnalystNoProposals);
        Assert.Equal(0, result.AnalystFailures);
    }

    [Fact]
    public async Task AnalyzeAsync_AnalystFails_ParseError_Reports_ParseFailures()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];
        fixture.AnalystAgent.Output = new ImprovementAnalysisOutput
        {
            Succeeded = false,
            FailureKind = AnalystOutputKind.ParseFailed,
            ErrorMessage = "no PROPOSAL_TITLE"
        };

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(0, result.ProposalsCreated);
        Assert.Equal(1, result.ParseFailures);
        Assert.Equal(0, result.AnalystFailures);
    }

    [Fact]
    public async Task AnalyzeAsync_EvaluationThrows_Reports_EvaluationFailures()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];
        fixture.Orchestrator.EvaluatorOverride = (_, _, _) => throw new InvalidOperationException("simulated eval failure");

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(0, result.ProposalsCreated);
        Assert.Equal(1, result.EvaluationFailures);
    }

    [Fact]
    public async Task AnalyzeAsync_PersistenceFails_Reports_PersistenceFailures_NotCountedAsCreated()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];
        fixture.ProposalRepository.ThrowOnAdd = true;

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(0, result.ProposalsCreated);
        Assert.Equal(1, result.PersistenceFailures);
        Assert.Empty(fixture.ProposalRepository.Proposals);
    }

    [Fact]
    public async Task AnalyzeAsync_Summary_IncludesNonZeroAnalystFailures()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];
        fixture.AnalystAgent.Output = new ImprovementAnalysisOutput
        {
            Succeeded = false,
            FailureKind = AnalystOutputKind.ProcessFailed,
            ErrorMessage = "process error"
        };

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Contains("Analyst failures: 1", result.Summary);
    }

    [Fact]
    public async Task AnalyzeAsync_Summary_AlwaysIncludesDetectedCreatedDuplicates()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];
        fixture.AnalystAgent.Output = new ImprovementAnalysisOutput
        {
            Succeeded = false,
            FailureKind = AnalystOutputKind.ProcessFailed,
            ErrorMessage = "process error"
        };

        var result = await fixture.Orchestrator.AnalyzeAsync();

        Assert.Contains("Detected: 1", result.Summary);
        Assert.Contains("Created: 0", result.Summary);
        Assert.Contains("Duplicates: 0", result.Summary);
    }

    [Fact]
    public async Task AnalyzeAsync_InvalidRiskLevelFromAgent_DefaultsToMedium()
    {
        var fixture = Build();
        fixture.AnalysisService.Patterns = [MakePattern()];

        var output = new ImprovementAnalysisOutput
        {
            Succeeded = true,
            ProposalTitle = "Title",
            TargetArea = "Area",
            RiskLevel = "not-a-risk-level",
            SuggestedChange = "change",
            Description = "desc"
        };
        fixture.AnalystAgent.Output = output;

        await fixture.Orchestrator.AnalyzeAsync();

        Assert.Equal(RiskLevel.Medium, fixture.ProposalRepository.Proposals.Single().RiskLevel);
    }

    // ── ApproveAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_ProposalNotFound_ReturnsFail()
    {
        var fixture = Build();

        var result = await fixture.Orchestrator.ApproveAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyRejected_ReturnsFail()
    {
        var fixture = Build();
        var proposal = AwaitingApprovalProposal();
        proposal.Reject();
        fixture.ProposalRepository.Proposals.Add(proposal);

        var result = await fixture.Orchestrator.ApproveAsync(proposal.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ApproveAsync_AwaitingApproval_CreatesNormalTaskAndSetsApproved()
    {
        var fixture = Build();
        var proposal = AwaitingApprovalProposal();
        fixture.ProposalRepository.Proposals.Add(proposal);

        var result = await fixture.Orchestrator.ApproveAsync(proposal.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(1, fixture.TaskService.CreateTaskCallCount);
        Assert.Equal(ImprovementProposalStatus.Approved, proposal.Status);
        Assert.NotNull(proposal.CreatedTaskId);
    }

    [Fact]
    public async Task ApproveAsync_UsesProposalTargetProjectId_NotAPlaceholder()
    {
        var fixture = Build();
        var proposal = AwaitingApprovalProposal(targetProjectId: "sandbox");
        fixture.ProposalRepository.Proposals.Add(proposal);

        await fixture.Orchestrator.ApproveAsync(proposal.Id);

        Assert.Equal("sandbox", fixture.TaskService.LastInput!.ProjectId);
        Assert.NotEqual("default", fixture.TaskService.LastInput.ProjectId);
    }

    [Fact]
    public async Task ApproveAsync_UnregisteredTargetProjectId_FailsSafelyWithoutCreatingTask()
    {
        // Registry only knows about "sandbox"; the proposal targets a project that was never registered.
        var fixture = Build(registeredProjects: [SandboxProject]);
        var proposal = AwaitingApprovalProposal(targetProjectId: "never-registered");
        fixture.ProposalRepository.Proposals.Add(proposal);

        var result = await fixture.Orchestrator.ApproveAsync(proposal.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not registered", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.TaskService.CreateTaskCallCount);
        Assert.Equal(ImprovementProposalStatus.AwaitingApproval, proposal.Status);
        Assert.Equal(0, fixture.ProposalRepository.UpdateCallCount);
    }

    [Fact]
    public async Task ApproveAsync_TargetProjectRemovedFromConfigSinceProposalWasCreated_FailsSafelyWithoutCreatingTask()
    {
        // Models a project that was registered when the proposal was created but has since been
        // removed from Projects:Projects config — this codebase has no separate enabled/disabled
        // flag on ProjectDefinition, so "disabled" and "unregistered" resolve through the same
        // IProjectRegistry.Find(...) is null check.
        var fixture = Build(registeredProjects: []); // registry now empty — project was "disabled"
        var proposal = AwaitingApprovalProposal(targetProjectId: "sandbox");
        fixture.ProposalRepository.Proposals.Add(proposal);

        var result = await fixture.Orchestrator.ApproveAsync(proposal.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(0, fixture.TaskService.CreateTaskCallCount);
        Assert.Equal(ImprovementProposalStatus.AwaitingApproval, proposal.Status);
    }

    [Fact]
    public async Task ApproveAsync_FailedTaskCreation_DoesNotMarkProposalApproved()
    {
        var fixture = Build();
        fixture.TaskService.ThrowOnCreate = true;
        var proposal = AwaitingApprovalProposal();
        fixture.ProposalRepository.Proposals.Add(proposal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Orchestrator.ApproveAsync(proposal.Id));

        Assert.Equal(ImprovementProposalStatus.AwaitingApproval, proposal.Status);
        Assert.Null(proposal.CreatedTaskId);
        Assert.Equal(0, fixture.ProposalRepository.UpdateCallCount);
    }

    [Fact]
    public async Task ApproveAsync_NeverRunsTheCreatedTaskAutomatically()
    {
        // The fake ITaskService only exposes CreateTaskAsync (and other query/transition
        // methods that all throw if called) — there is no "run" capability reachable from
        // the orchestrator's dependency surface at all, so this asserts the created task
        // is left exactly in its just-created state with no further calls made.
        var fixture = Build();
        var proposal = AwaitingApprovalProposal();
        fixture.ProposalRepository.Proposals.Add(proposal);

        await fixture.Orchestrator.ApproveAsync(proposal.Id);

        Assert.Equal(AgentTaskStatus.Created, fixture.TaskService.CreatedTask!.Status);
    }

    [Fact]
    public async Task ApproveAsync_CalledTwice_DoesNotCreateSecondTask()
    {
        var fixture = Build();
        var proposal = AwaitingApprovalProposal();
        fixture.ProposalRepository.Proposals.Add(proposal);

        var first = await fixture.Orchestrator.ApproveAsync(proposal.Id);
        var second = await fixture.Orchestrator.ApproveAsync(proposal.Id);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(1, fixture.TaskService.CreateTaskCallCount);
        Assert.Equal(first.Proposal!.CreatedTaskId, second.Proposal!.CreatedTaskId);
    }

    [Fact]
    public async Task ApproveAsync_PersistsCreatedTaskIdOnTheProposal()
    {
        var fixture = Build();
        var proposal = AwaitingApprovalProposal();
        fixture.ProposalRepository.Proposals.Add(proposal);

        var result = await fixture.Orchestrator.ApproveAsync(proposal.Id);

        Assert.NotNull(result.Proposal!.CreatedTaskId);
        Assert.Equal(result.Proposal.CreatedTaskId, fixture.TaskService.CreatedTask!.Id);
        Assert.True(fixture.ProposalRepository.UpdateCallCount >= 1);
    }

    [Fact]
    public async Task ApproveAsync_ProposalDescription_IncludesEvidenceAndSuggestedChange()
    {
        var fixture = Build();
        var proposal = AwaitingApprovalProposal();
        fixture.ProposalRepository.Proposals.Add(proposal);

        await fixture.Orchestrator.ApproveAsync(proposal.Id);

        Assert.Contains(proposal.SuggestedChange, fixture.TaskService.LastInput!.Description);
        Assert.Contains(proposal.Evidence, fixture.TaskService.LastInput.Description);
    }

    // ── RejectAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectAsync_AwaitingApproval_SetsRejected()
    {
        var fixture = Build();
        var proposal = AwaitingApprovalProposal();
        fixture.ProposalRepository.Proposals.Add(proposal);

        var result = await fixture.Orchestrator.RejectAsync(proposal.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(ImprovementProposalStatus.Rejected, proposal.Status);
    }

    [Fact]
    public async Task RejectAsync_CalledTwice_IsIdempotent()
    {
        var fixture = Build();
        var proposal = AwaitingApprovalProposal();
        fixture.ProposalRepository.Proposals.Add(proposal);

        var first = await fixture.Orchestrator.RejectAsync(proposal.Id);
        var second = await fixture.Orchestrator.RejectAsync(proposal.Id);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task RejectAsync_AlreadyApproved_ReturnsFail()
    {
        var fixture = Build();
        var proposal = AwaitingApprovalProposal();
        proposal.Approve(Guid.NewGuid());
        fixture.ProposalRepository.Proposals.Add(proposal);

        var result = await fixture.Orchestrator.RejectAsync(proposal.Id);

        Assert.False(result.Succeeded);
    }

    // ── Read-only queries ────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetricsAsync_DelegatesToMetricsCalculator()
    {
        var fixture = Build();
        fixture.MetricsCalculator.Snapshot = new AgentMetricsSnapshot(
            totalTasks: 5, taskSuccessRate: 0.8, developerFailureRate: 0.1, qaPassRate: 0.9,
            reviewApprovalRate: 0.9, averageRetriesPerTask: 0.5, releaseFailureRate: 0,
            packageFailureRate: 0, failuresByCategory: new Dictionary<FailureCategory, int>());

        var snapshot = await fixture.Orchestrator.GetMetricsAsync();

        Assert.Equal(5, snapshot.TotalTasks);
    }

    [Fact]
    public async Task GetRecentFailuresAsync_RespectsCount()
    {
        var fixture = Build();
        fixture.FailureRepository.Failures =
        [
            new ExecutionFailure(Guid.NewGuid(), null, FailureCategory.BuildFailure, "BackendDeveloper", "1"),
            new ExecutionFailure(Guid.NewGuid(), null, FailureCategory.BuildFailure, "BackendDeveloper", "2"),
            new ExecutionFailure(Guid.NewGuid(), null, FailureCategory.BuildFailure, "BackendDeveloper", "3")
        ];

        var failures = await fixture.Orchestrator.GetRecentFailuresAsync(2);

        Assert.Equal(2, failures.Count);
    }

    [Fact]
    public async Task GetProposalsAsync_ReturnsAllProposals()
    {
        var fixture = Build();
        fixture.ProposalRepository.Proposals.Add(AwaitingApprovalProposal());
        fixture.ProposalRepository.Proposals.Add(AwaitingApprovalProposal());

        var proposals = await fixture.Orchestrator.GetProposalsAsync();

        Assert.Equal(2, proposals.Count);
    }

    [Fact]
    public async Task GetProposalAsync_UnknownId_ReturnsNull()
    {
        var fixture = Build();

        var proposal = await fixture.Orchestrator.GetProposalAsync(Guid.NewGuid());

        Assert.Null(proposal);
    }

    // ── Architectural boundary: no git/worktree/process capability ─────────────

    [Fact]
    public void Constructor_HasNoGitOrWorktreeOrProcessCapableDependency()
    {
        // ImprovementOrchestrator must never be able to touch git, worktrees, or run arbitrary
        // processes — it only reads Core repositories, calls the read-only analyst agent, and
        // creates a normal AgentTask via ITaskService. This is a structural guarantee: assert
        // none of its constructor parameters are a git/worktree/process-capable type at all,
        // so there is nothing for a compromised evidence payload to abuse even in principle.
        var forbiddenTypes = new[] { typeof(IWorkspaceManager), typeof(IProcessRunner) };

        var constructor = typeof(ImprovementOrchestrator).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToList();

        foreach (var forbidden in forbiddenTypes)
            Assert.DoesNotContain(forbidden, parameterTypes);
    }

    private static ImprovementProposal AwaitingApprovalProposal(string targetProjectId = "sandbox")
    {
        var proposal = new ImprovementProposal(
            targetProjectId,
            "Developer repeatedly fails the build",
            "The Developer agent has failed the build 3 times for the same reason.",
            "3 build failures in the last week.",
            "Developer Prompt",
            "Add an explicit build-before-commit reminder to the Developer prompt.",
            RiskLevel.Low,
            $"FP-{Guid.NewGuid()}");
        proposal.BeginEvaluation();
        proposal.CompleteEvaluation("3/3 regression cases passed (100%).");
        return proposal;
    }
}
