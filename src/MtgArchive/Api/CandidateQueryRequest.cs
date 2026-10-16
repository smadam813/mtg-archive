using System.ComponentModel;
using MtgArchive.Data;

namespace MtgArchive.Api;

/// <summary>
/// Base request model for every candidate-producing query — the inputs the
/// legality boundary needs, resolved once by <see cref="CandidateQuery"/>.
/// The no-illegal-candidates harness discovers index endpoints by this base
/// type, so deriving from it is what puts an endpoint under the property
/// test. Collection scope is a per-request body field — never a header or
/// global toggle. Under scope, availability = min(copy limit, owned
/// quantity).
/// </summary>
public abstract record CandidateQueryRequest
{
    [Description("Hard-filter candidates to owned cards (the collection). Set by the caller, "
        + "never inferred. Under scope a candidate's availability is min(copy limit, owned "
        + "quantity).")]
    public bool CollectionScope { get; init; } = false;

    [Description("The format candidates are judged in: commander, brawl, competitive_brawl, "
        + "standard_brawl, or standard. Commander when omitted, "
        + "and the versions block echoes it. An unknown format is a 400, never a silent fallback.")]
    public string? Format { get; init; }

    [Description("Banlist version pin (effective_date, yyyy-MM-dd); latest when omitted. "
        + "An unknown pin is a 400, never a silent fallback.")]
    public string? BanlistVersion { get; init; }

    [Description("The seat candidates are judged for; legality is per role.")]
    public Role Role { get; init; } = Role.Main;

    [Description("Deck color identity as letters (W, U, B, R, G): only cards whose identity "
        + "fits inside it are candidates; [] admits colorless cards only. Omit for no "
        + "color restriction.")]
    public string[]? ColorIdentity { get; init; }
}
