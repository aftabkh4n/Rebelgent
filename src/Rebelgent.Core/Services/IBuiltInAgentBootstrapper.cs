namespace Rebelgent.Core.Services;

/// <summary>
/// Imports the built-in Rebelgent agents from M1-M9 into the governed
/// <see cref="Rebelgent.Core.Domain.AgentDefinition"/> registry.
///
/// This is a compatibility bootstrap only. It represents pre-M10 operational agents
/// as Active without going through the human-authorized activation path. This
/// exception applies ONLY to importing pre-existing built-ins — the general rule
/// that newly proposed agents require human approval before activation is UNCHANGED.
/// Idempotent: running the bootstrap repeatedly does not create duplicates.
/// </summary>
public interface IBuiltInAgentBootstrapper
{
    Task<BuiltInAgentBootstrapResult> EnsureBootstrappedAsync(CancellationToken ct = default);
}

/// <summary>Outcome of a bootstrap invocation.</summary>
public sealed record BuiltInAgentBootstrapResult(int TotalBuiltIns, int Imported, int AlreadyPresent);
