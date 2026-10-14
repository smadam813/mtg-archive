namespace MtgArchive.CollectionImport;

public enum NameResolutionOutcome
{
    Resolved,
    Unresolved,
    Ambiguous,
}

/// <summary>
/// The three-way classification the resolver gives one card name: resolved
/// to a single <c>oracle_id</c>, unresolved (no corpus match), or ambiguous
/// (the name belongs to several oracle identities — mostly token names like
/// Elemental — with the candidates listed for the quarantine report). An
/// ambiguous name is never resolved by picking one.
/// </summary>
public sealed record NameResolution
{
    public required NameResolutionOutcome Outcome { get; init; }

    /// <summary>The single match; set only when <see cref="Outcome"/> is Resolved.</summary>
    public Guid? OracleId { get; init; }

    /// <summary>Every match; set only when <see cref="Outcome"/> is Ambiguous.</summary>
    public IReadOnlyList<Guid> CandidateOracleIds { get; init; } = [];

    public static NameResolution Resolved(Guid oracleId)
        => new() { Outcome = NameResolutionOutcome.Resolved, OracleId = oracleId };

    public static readonly NameResolution Unresolved
        = new() { Outcome = NameResolutionOutcome.Unresolved };

    public static NameResolution Ambiguous(IEnumerable<Guid> candidates)
        => new() { Outcome = NameResolutionOutcome.Ambiguous, CandidateOracleIds = [.. candidates] };
}
