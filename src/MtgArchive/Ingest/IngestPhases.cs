using System.Collections.Immutable;

namespace MtgArchive.Ingest;

/// <summary>
/// The canonical ingest phase order. Every run records one slot per phase;
/// a phase without a registered <see cref="IIngestPhase"/> implementation is
/// a slot for a later issue and completes as skipped.
/// </summary>
public static class IngestPhases
{
    public const string FetchValidate = "fetch_validate";
    public const string CardUpsert = "card_upsert";
    public const string SourceProfileDerive = "source_profile_derive";
    public const string CardFeatureDerive = "card_feature_derive";
    public const string CrRefresh = "cr_refresh";
    public const string BanlistReplay = "banlist_replay";
    public const string LegalityRebuild = "legality_rebuild";
    public const string PopularityLoad = "popularity_load";
    public const string BracketLoad = "bracket_load";
    public const string ReEmbed = "re_embed";

    public static readonly ImmutableArray<string> CanonicalOrder =
    [
        FetchValidate,
        CardUpsert,
        SourceProfileDerive,
        CardFeatureDerive,
        CrRefresh,
        BanlistReplay,
        LegalityRebuild,
        PopularityLoad,
        BracketLoad,
        ReEmbed,
    ];
}
