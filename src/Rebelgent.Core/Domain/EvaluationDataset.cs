namespace Rebelgent.Core.Domain;

/// <summary>A named, in-memory collection of historical failure examples used to regression-test
/// an <see cref="ImprovementProposal"/> before it can move to AwaitingApproval. Built locally from
/// persisted <see cref="ExecutionFailure"/> records — requires no external or paid service.</summary>
public sealed class EvaluationDataset
{
    public string Name { get; }
    public IReadOnlyList<EvaluationCase> Cases { get; }

    public EvaluationDataset(string name, IReadOnlyList<EvaluationCase> cases)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Dataset name cannot be empty.", nameof(name));

        Name = name;
        Cases = cases ?? throw new ArgumentNullException(nameof(cases));
    }
}
