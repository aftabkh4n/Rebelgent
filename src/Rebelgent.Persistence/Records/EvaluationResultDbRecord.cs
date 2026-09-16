using Rebelgent.Core.Domain;

namespace Rebelgent.Persistence.Records;

/// <summary>EF Core persistence record for <see cref="EvaluationResult"/>.</summary>
internal class EvaluationResultDbRecord
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public string DatasetName { get; set; } = string.Empty;
    public int TotalCases { get; set; }
    public int PassedCases { get; set; }
    public double PassRate { get; set; }
    public string? Notes { get; set; }
    public string CasesJson { get; set; } = "[]";
    public long EvaluatedAt { get; set; }

    public static EvaluationResultDbRecord FromDomain(EvaluationResult result) => new()
    {
        Id = result.Id,
        ProposalId = result.ProposalId,
        DatasetName = result.DatasetName,
        TotalCases = result.TotalCases,
        PassedCases = result.PassedCases,
        PassRate = result.PassRate,
        Notes = result.Notes,
        CasesJson = result.CasesJson,
        EvaluatedAt = result.EvaluatedAt.UtcTicks
    };

    public EvaluationResult ToDomain() => EvaluationResult.Reconstitute(
        Id, ProposalId, DatasetName, TotalCases, PassedCases, PassRate, Notes, CasesJson,
        new DateTimeOffset(EvaluatedAt, TimeSpan.Zero));
}
