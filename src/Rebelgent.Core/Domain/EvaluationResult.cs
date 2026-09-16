using System.Text.Json;

namespace Rebelgent.Core.Domain;

/// <summary>The persisted outcome of evaluating an <see cref="ImprovementProposal"/> against an
/// <see cref="EvaluationDataset"/>. Evaluation is purely data-driven — it never executes code or
/// modifies production behavior.</summary>
public class EvaluationResult
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public Guid Id { get; private set; }
    public Guid ProposalId { get; private set; }
    public string DatasetName { get; private set; }
    public int TotalCases { get; private set; }
    public int PassedCases { get; private set; }
    public double PassRate { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset EvaluatedAt { get; private set; }

    internal string CasesJson { get; private set; }

    public IReadOnlyList<EvaluationCase> Cases =>
        JsonSerializer.Deserialize<List<EvaluationCase>>(CasesJson, JsonOptions) ?? [];

    public EvaluationResult(Guid proposalId, string datasetName, IReadOnlyList<EvaluationCase> cases, string? notes)
    {
        if (proposalId == Guid.Empty)
            throw new ArgumentException("Proposal ID cannot be empty.", nameof(proposalId));
        if (string.IsNullOrWhiteSpace(datasetName))
            throw new ArgumentException("Dataset name cannot be empty.", nameof(datasetName));
        if (cases is null)
            throw new ArgumentNullException(nameof(cases));

        Id = Guid.NewGuid();
        ProposalId = proposalId;
        DatasetName = datasetName;
        TotalCases = cases.Count;
        PassedCases = cases.Count(c => c.Passed);
        PassRate = TotalCases == 0 ? 0d : PassedCases / (double)TotalCases;
        Notes = notes;
        CasesJson = JsonSerializer.Serialize(cases, JsonOptions);
        EvaluatedAt = DateTimeOffset.UtcNow;
    }

    internal static EvaluationResult Reconstitute(
        Guid id, Guid proposalId, string datasetName, int totalCases, int passedCases,
        double passRate, string? notes, string casesJson, DateTimeOffset evaluatedAt)
    {
        return new EvaluationResult
        {
            Id = id,
            ProposalId = proposalId,
            DatasetName = datasetName,
            TotalCases = totalCases,
            PassedCases = passedCases,
            PassRate = passRate,
            Notes = notes,
            CasesJson = casesJson,
            EvaluatedAt = evaluatedAt
        };
    }

    private EvaluationResult()
    {
        DatasetName = string.Empty;
        CasesJson = "[]";
    }
}
