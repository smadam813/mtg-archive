using System.Text.Json;
using System.Text.Json.Serialization;
using MtgArchive.Api;
using MtgArchive.CollectionImport;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// The run's outcome record — the ingest-side sibling of the quarantine
/// report. Fourteen items: new/changed card counts, drift detections,
/// CR version found, stale cards, collection rows pointing at stale cards,
/// unmatched search-lands, unmatched enters-tapped lands, unmatched opponent
/// or combat sources, unprofiled card faces, response card faces, each Brawl format's legality
/// counts and status changes, skipped popularity export rows, errors. The run carries the
/// report and never drops an item. Drift detections carry every detector's entries; <c>kind</c>
/// tells them apart (<see cref="Banlist.BanlistDriftDetection"/>,
/// <see cref="Bracket.GameChangersDriftDetection"/>); collection rows pointing at stale
/// cards come from the card upsert phase.
/// </summary>
public sealed record IngestReport(
    int NewCards,
    int ChangedCards,
    IReadOnlyList<JsonElement> DriftDetections,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CrVersionFound,
    IReadOnlyList<StaleCardReport> StaleCards,
    StaleCollectionRowsReport CollectionRowsPointingAtStaleCards,
    IReadOnlyList<UnmatchedSearchLand> UnmatchedSearchLands,
    IReadOnlyList<UnmatchedEntersTappedLand> UnmatchedEntersTappedLands,
    IReadOnlyList<UnmatchedOpponentSource> UnmatchedOpponentSources,
    IReadOnlyList<UnprofiledCard> UnprofiledCards,
    IReadOnlyList<ResponseCard> ResponseCards,
    IReadOnlyList<BrawlLegalityReport> BrawlLegality,
    IReadOnlyList<SkippedPopularityRow> SkippedPopularityRows,
    IReadOnlyList<string> Errors)
{
    /// <summary>Serializes to the wire/jsonb shape (snake_case).</summary>
    public JsonDocument ToDocument() => JsonSerializer.SerializeToDocument(this, WireJson.Options);
}

public sealed record StaleCardReport(Guid OracleId, string Name);

/// <summary>A card whose search-land text falls outside the closed fetch
/// template family — it got no fetch profile (the source-profile phase never
/// guesses), and this entry surfaces it for human curation.</summary>
public sealed record UnmatchedSearchLand(Guid OracleId, string Name);

/// <summary>A land whose "enters tapped" text is unmatched by the closed
/// template family, or whose land faces disagree. It has no enters-tapped
/// facet, and this entry surfaces it for human curation.</summary>
public sealed record UnmatchedEntersTappedLand(Guid OracleId, string Name);

/// <summary>A nonland card with a mana-producing or fetch ability that
/// mentions an opponent or combat outside the closed opponent-dependent template family.
/// The card gets no opponent-dependent facet, so tier 1 still counts it as a
/// source. This entry lists it for a human to check.</summary>
public sealed record UnmatchedOpponentSource(Guid OracleId, string Name);

/// <summary>A card face with a sentence that mentions draw, search or add
/// and matches no closed card-feature template. The face keeps only the
/// features the templates proved, engine coverage lists the card as
/// unprofiled, and this entry surfaces the sentence for human curation.</summary>
public sealed record UnprofiledCard(Guid OracleId, string Name, string Face, string Sentence);

/// <summary>A card face with a response feature (ADR 0013), or with a
/// sentence that mentions an answer outside the response families. A
/// profiled entry has no sentence. An unprofiled entry names its first such
/// sentence for human curation; the face keeps only the response features
/// the families proved, and the answered rate is a lower bound.</summary>
public sealed record ResponseCard(Guid OracleId, string Name, string Face, bool Profiled, string? Sentence);

/// <summary>
/// One Brawl format after the legality rebuild (ADR 0012). The counts read
/// the main role, where each card has one status. <paramref name="StatusChanges"/>
/// lists each card whose main-role status differs from the prior ingest: a
/// ban, an unban, or a rotation. A card with no prior row is not a change.
/// Drift stays a Commander term, so a Brawl format has no drift entry.
/// </summary>
public sealed record BrawlLegalityReport(
    Format Format,
    DateTimeOffset LegalityAsOf,
    int Legal,
    int Banned,
    int NotLegal,
    IReadOnlyList<BrawlStatusChange> StatusChanges);

/// <summary>
/// A popularity export row with stats that the popularity load phase did
/// not load (ADR 0012). Its name matches no card in the format's pool
/// (<see cref="SkippedPopularityReason.NoMatch"/>), more than one
/// (<see cref="SkippedPopularityReason.Ambiguous"/>), or a card whose seat
/// an earlier row already filled (<see cref="SkippedPopularityReason.Duplicate"/>).
/// The name is the export's, verbatim.
/// </summary>
public sealed record SkippedPopularityRow(Format Format, string Name, Role Seat, SkippedPopularityReason Reason);

/// <summary>Why the popularity load skipped an export row.</summary>
public enum SkippedPopularityReason
{
    NoMatch,
    Ambiguous,
    Duplicate,
}

/// <summary>A card whose main-role status in one Brawl format changed since
/// the prior ingest.</summary>
public sealed record BrawlStatusChange(Guid OracleId, string Name, LegalityStatus From, LegalityStatus To);

/// <summary>
/// Collection rows whose <c>oracle_id</c> points at a stale card. The rows
/// stay valid — queries exclude stale cards downstream; this is the notice.
/// <paramref name="Populated"/> is false only when the run failed before the
/// card upsert phase measured it.
/// </summary>
public sealed record StaleCollectionRowsReport(bool Populated, IReadOnlyList<CollectionCardRow> Rows);

/// <summary>Accumulates report content as phases run.</summary>
public sealed class IngestReportBuilder
{
    public int NewCards { get; set; }

    public int ChangedCards { get; set; }

    public List<JsonElement> DriftDetections { get; } = [];

    public string? CrVersionFound { get; set; }

    public List<StaleCardReport> StaleCards { get; } = [];

    /// <summary>Null until the card upsert phase measures it.</summary>
    public List<CollectionCardRow>? StaleCollectionRows { get; set; }

    public List<UnmatchedSearchLand> UnmatchedSearchLands { get; } = [];

    public List<UnmatchedEntersTappedLand> UnmatchedEntersTappedLands { get; } = [];

    public List<UnmatchedOpponentSource> UnmatchedOpponentSources { get; } = [];

    public List<UnprofiledCard> UnprofiledCards { get; } = [];

    public List<ResponseCard> ResponseCards { get; } = [];

    public List<BrawlLegalityReport> BrawlLegality { get; } = [];

    public List<SkippedPopularityRow> SkippedPopularityRows { get; } = [];

    public List<string> Errors { get; } = [];

    public IngestReport Build() => new(
        NewCards,
        ChangedCards,
        DriftDetections,
        CrVersionFound,
        StaleCards,
        new StaleCollectionRowsReport(
            Populated: StaleCollectionRows is not null,
            Rows: StaleCollectionRows ?? []),
        UnmatchedSearchLands,
        UnmatchedEntersTappedLands,
        UnmatchedOpponentSources,
        UnprofiledCards,
        ResponseCards,
        BrawlLegality,
        SkippedPopularityRows,
        Errors);
}
