using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Data;

/// <summary>
/// The single Postgres store (pgvector/pgvector:pg18): corpus, index,
/// collection, and simulation artifacts in one database. EF Core migrations
/// are the sole owner of DDL and apply automatically on service startup.
///
/// Raw-SQL convention: exactly two query paths use raw SQL — the Search
/// surfaces' KNN statements (card hybrid search, rules search) and the
/// legality set-returning function (<see cref="LegalCandidates"/>)
/// — always parameterized via EF (<c>SqlQuery</c>/<c>FromSql</c>
/// interpolation, never string concatenation). Every other query goes
/// through the model; <see cref="AvailableCopies"/> is a model-mapped SQL
/// function, not raw SQL. Two non-query exceptions: the
/// derived-state staging machinery (<see cref="Ingest.DerivedStateSwap"/>
/// and its callers' staging copies) runs raw TRUNCATE / INSERT…SELECT /
/// binary COPY / ALTER…RENAME over migration-owned tables, built from
/// compile-time spec identifiers. The renames exchange live and staging
/// twins and restore the names the migrations declared, so the schema at
/// rest stays what migrations own. And the collection import takes a
/// transaction-scoped advisory lock (<c>pg_advisory_xact_lock</c>) so full
/// replaces never interleave.
///
/// Hybrid query law: structured predicates compose in-SQL with the pgvector
/// KNN as <c>WHERE &lt;filters&gt; ORDER BY embedding &lt;=&gt; $q LIMIT k</c>,
/// relying on pgvector 0.8 iterative index scans. App-side post-filtering is
/// never allowed. Card search instead orders each card by its nearest face
/// (<c>ORDER BY min(embedding &lt;=&gt; $q)</c>) and scans every candidate.
/// </summary>
public sealed class ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : DbContext(options)
{
    public DbSet<Card> Cards => Set<Card>();

    public DbSet<CardFace> CardFaces => Set<CardFace>();

    public DbSet<CardRuling> CardRulings => Set<CardRuling>();

    public DbSet<CardOracleTag> CardOracleTags => Set<CardOracleTag>();

    public DbSet<TagAlias> TagAliases => Set<TagAlias>();

    public DbSet<CardFaceDocument> CardFaceDocuments => Set<CardFaceDocument>();

    public DbSet<EmbeddingState> EmbeddingStates => Set<EmbeddingState>();

    public DbSet<IngestRun> IngestRuns => Set<IngestRun>();

    public DbSet<CorpusState> CorpusStates => Set<CorpusState>();

    public DbSet<BanlistVersion> BanlistVersions => Set<BanlistVersion>();

    public DbSet<BanEvent> BanEvents => Set<BanEvent>();

    public DbSet<Legality> Legalities => Set<Legality>();

    public DbSet<CrDocument> CrDocuments => Set<CrDocument>();

    public DbSet<CrRuleChunk> CrRuleChunks => Set<CrRuleChunk>();

    public DbSet<CrGlossaryChunk> CrGlossaryChunks => Set<CrGlossaryChunk>();

    public DbSet<RulesReembedSignal> RulesReembedSignals => Set<RulesReembedSignal>();

    public DbSet<SourceProfile> SourceProfiles => Set<SourceProfile>();

    public DbSet<CardFeatureProfileRow> CardFeatureProfileRows => Set<CardFeatureProfileRow>();

    public DbSet<CollectionCard> CollectionCards => Set<CollectionCard>();

    public DbSet<CollectionImportRow> CollectionImportRows => Set<CollectionImportRow>();

    public DbSet<CollectionImportState> CollectionImportStates => Set<CollectionImportState>();

    public DbSet<BracketSpecVersion> BracketSpecVersions => Set<BracketSpecVersion>();

    public DbSet<GameChangersSnapshot> GameChangersSnapshots => Set<GameChangersSnapshot>();

    public DbSet<BarometerCard> BarometerCards => Set<BarometerCard>();

    public DbSet<PopularityRank> PopularityRanks => Set<PopularityRank>();

    public DbSet<PopularitySource> PopularitySources => Set<PopularitySource>();

    public DbSet<CardPopularity> CardPopularities => Set<CardPopularity>();

    /// <summary>
    /// The legality boundary: every card whose materialized legality is
    /// <c>legal</c> in <paramref name="format"/> and <paramref name="role"/>.
    /// Commander reads the banlist version dated
    /// <paramref name="banlistVersion"/> (null — nothing replayed yet —
    /// yields nothing); a Brawl format has no version axis and reads its one
    /// current state under a null <paramref name="banlistVersion"/> (a date
    /// yields nothing). Stale cards excluded, and when
    /// <paramref name="colorIdentityMask"/> is given only cards whose
    /// identity fits inside it (one bitwise AND,
    /// <see cref="Card.ColorIdentityMaskContract"/>); under
    /// <paramref name="collectionScope"/> only owned cards (a
    /// <c>collection_card</c> row), the same hard filter as legality. The
    /// only path to candidate card rows: index queries start here (via
    /// <see cref="Api.CandidateQuery"/>) and compose their predicates and
    /// the pgvector KNN onto the result. Postgres inlines the function, so
    /// the composed query plans as one join over
    /// <c>ix_legality_version_format_role</c>.
    /// </summary>
    public IQueryable<Card> LegalCandidates(
        Format format, DateOnly? banlistVersion, Role role, int? colorIdentityMask = null, bool collectionScope = false)
        => Cards.FromSql(
            $"SELECT * FROM legal_candidates({WireName(format)}, {banlistVersion}::date, {WireName(role)}, {colorIdentityMask}::integer, {collectionScope})");

    /// <summary>
    /// Scoped availability, composed in SQL (<c>available_copies</c>):
    /// min(copy limit, owned quantity) — an unlimited copy limit (null)
    /// yields the owned quantity, an unowned card null. No infinite-basics
    /// assumption: owning 3 Forests means 3 available.
    /// </summary>
    public static int? AvailableCopies(int? copyLimit, Guid oracleId)
        => throw new InvalidOperationException("Composed into SQL by EF; never called in process.");

    // Every embeddable table stores the primary model's width
    // (qwen3-embedding:0.6b, 1024 dims); a narrower model is zero-padded to
    // it by OllamaEmbedder, and EmbeddingModel.Resolve refuses a wider one
    // at startup. Each column carries an HNSW cosine-distance (<=>) index,
    // the operator every KNN uses.
    public const int EmbeddingDimensions = 1024;

    private static readonly string EmbeddingColumnType = $"vector({EmbeddingDimensions})";

    // Table and column names are snake_case regardless of how the options
    // were built, so tests and tools cannot drift from the migrations.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasDbFunction(() => AvailableCopies(default, default)).HasName("available_copies");

        modelBuilder.Entity<Card>(card =>
        {
            card.ToTable("card");
            // The natural key the collection FKs target. Its unique
            // constraint replaced the original ix_card_oracle_id index —
            // the same guarantee, one index instead of two.
            card.HasAlternateKey(c => c.OracleId);
            // Exact-name lookup (GET /cards?name=) seeks this; not unique —
            // a stale row may share its name with its live replacement.
            card.HasIndex(c => c.Name);
            card.Property(c => c.ColorIdentityMask)
                .HasComputedColumnSql(
                    "(CASE WHEN color_identity @> ARRAY['W'::text] THEN 16 ELSE 0 END)"
                    + " | (CASE WHEN color_identity @> ARRAY['U'::text] THEN 8 ELSE 0 END)"
                    + " | (CASE WHEN color_identity @> ARRAY['B'::text] THEN 4 ELSE 0 END)"
                    + " | (CASE WHEN color_identity @> ARRAY['R'::text] THEN 2 ELSE 0 END)"
                    + " | (CASE WHEN color_identity @> ARRAY['G'::text] THEN 1 ELSE 0 END)",
                    stored: true)
                .HasComment(Card.ColorIdentityMaskContract);
            card.Property(c => c.Raw)
                .HasComment(Card.RawContract);
            card.Property(c => c.CanBeCommander)
                .HasComment(Card.CanBeCommanderContract);
            card.Property(c => c.CanBeBrawlCommander)
                .HasComment(Card.CanBeBrawlCommanderContract);
            card.Property(c => c.IsMeldResult)
                .HasComment(Card.IsMeldResultContract);
            card.Property(c => c.DigitalOnly)
                .HasComment(Card.DigitalOnlyContract);
            card.Property(c => c.HasPartner)
                .HasComment(Card.HasPartnerContract);
            card.Property(c => c.HasBackground)
                .HasComment(Card.HasBackgroundContract);
            // DEFAULT 1 covers inserts that bypass the model. The sentinel is
            // 0 (never a legal copy limit) rather than EF's null default for
            // int?, so an explicit NULL (= unlimited) still reaches the
            // database instead of being read as "not set".
            card.Property(c => c.CopyLimit)
                .HasDefaultValue(1)
                .HasSentinel(0)
                .HasComment(Card.CopyLimitContract);
            card.Property(c => c.ReleasedAt)
                .HasComment(Card.ReleasedAtContract);
            card.Property(c => c.Stale)
                .HasComment(Card.StaleContract);
        });

        modelBuilder.Entity<IngestRun>(run =>
        {
            run.ToTable("ingest_run");
            run.Property(r => r.Status)
                .HasConversion(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<IngestRunStatus>(s, true));
        });

        modelBuilder.Entity<IngestRunPhase>(phase =>
        {
            phase.ToTable("ingest_run_phase");
            phase.HasIndex(p => new { p.IngestRunId, p.Phase }).IsUnique();
            phase.HasOne<IngestRun>().WithMany(r => r.Phases).HasForeignKey(p => p.IngestRunId);
            phase.Property(p => p.Status)
                .HasConversion(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<IngestPhaseStatus>(s, true));
        });

        modelBuilder.Entity<CorpusState>(state => SingletonRow(state, "corpus_state"));

        modelBuilder.Entity<BanlistVersion>(version =>
        {
            version.ToTable("banlist_version");
            // One version per dated announcement; effective_date is the
            // version's wire address.
            version.HasIndex(v => v.EffectiveDate).IsUnique();
            version.Property(v => v.DatePrecision)
                .HasConversion(v => WireName(v), s => ParseWireName<DatePrecision>(s));
        });

        modelBuilder.Entity<BanEvent>(banEvent =>
        {
            banEvent.ToTable("ban_event");
            // A dated announcement changes a card at most once.
            banEvent.HasIndex(e => new { e.BanlistVersionId, e.CardId }).IsUnique();
            banEvent.HasIndex(e => e.CardId);
            banEvent.HasOne<BanlistVersion>().WithMany(v => v.Events).HasForeignKey(e => e.BanlistVersionId);
            // Ban history is dated citation — it must never vanish with a
            // card row (cards are marked stale, never deleted).
            banEvent.HasOne(e => e.Card).WithMany().HasForeignKey(e => e.CardId)
                .OnDelete(DeleteBehavior.Restrict);
            banEvent.Property(e => e.Action)
                .HasConversion(v => WireName(v), s => ParseWireName<BanAction>(s));
            banEvent.Property(e => e.Scope)
                .HasConversion(v => WireName(v), s => ParseWireName<BanScope>(s));
        });

        modelBuilder.Entity<CrDocument>(document =>
        {
            document.ToTable("cr_document");
            // The archive is keyed by the effective date parsed from the CR
            // file header — the natural version identity.
            document.HasKey(d => d.EffectiveDate);
        });

        modelBuilder.Entity<CrRuleChunk>(chunk =>
        {
            chunk.ToTable("cr_rule_chunk");
            chunk.HasIndex(c => c.Number).IsUnique();
            chunk.Property(c => c.Embedding).HasColumnType(EmbeddingColumnType);
            chunk.HasIndex(c => c.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });

        modelBuilder.Entity<CrGlossaryChunk>(chunk =>
        {
            chunk.ToTable("cr_glossary_chunk");
            chunk.HasIndex(c => c.Term).IsUnique();
            chunk.Property(c => c.Embedding).HasColumnType(EmbeddingColumnType);
            chunk.HasIndex(c => c.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });

        modelBuilder.Entity<CardFaceDocument>(document =>
        {
            document.ToTable("card_face_document");
            document.HasIndex(d => new { d.CardId, d.FaceIndex }).IsUnique();
            // Cascade, like card_face: the document is composed from the
            // card's faces and has no life of its own.
            document.HasOne<Card>().WithMany(c => c.Documents).HasForeignKey(d => d.CardId)
                .OnDelete(DeleteBehavior.Cascade);
            document.Property(d => d.Embedding).HasColumnType(EmbeddingColumnType);
            document.HasIndex(d => d.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });

        modelBuilder.Entity<EmbeddingState>(state => SingletonRow(state, "embedding_state"));

        modelBuilder.Entity<RulesReembedSignal>(signal => SingletonRow(signal, "rules_reembed_signal"));

        modelBuilder.Entity<CardFace>(face =>
        {
            face.ToTable("card_face");
            face.HasIndex(f => new { f.CardId, f.FaceIndex }).IsUnique();
            // The collection-import resolver's face-name fallback seeks this,
            // the same way exact card-name lookup seeks ix_card_name.
            face.HasIndex(f => f.Name);
            face.HasOne<Card>().WithMany(c => c.Faces).HasForeignKey(f => f.CardId);
        });

        modelBuilder.Entity<CardRuling>(ruling =>
        {
            ruling.ToTable("card_ruling");
            ruling.HasOne<Card>().WithMany(c => c.Rulings).HasForeignKey(r => r.CardId);
        });

        modelBuilder.Entity<CardOracleTag>(tag =>
        {
            tag.ToTable("card_oracle_tag");
            tag.HasKey(t => new { t.CardId, t.Tag });
            tag.HasOne<Card>().WithMany(c => c.OracleTags).HasForeignKey(t => t.CardId);
        });

        modelBuilder.Entity<TagAlias>(alias =>
        {
            alias.ToTable("tag_alias");
            alias.HasKey(a => a.Alias);
        });

        // Each derived-state table exists twice: live and a permanent
        // `_staging` twin. The producing phase rebuilds into staging and
        // publishes via DerivedStateSwap's rename-exchange, so every
        // constraint and index name is set explicitly — the swap renames them
        // in lockstep with the tables, keeping live names canonical (matching
        // this model) after any number of swaps.
        //
        // Legality is fully derived and rebuilt every ingest, so its FKs all
        // cascade: an upstream row the replay deletes (a transcription
        // correction retiring a ban event or version) takes its now-dangling
        // legality rows with it, and the same run's rebuild regenerates them.
        // Restrict would deadlock those corrections against the previous
        // run's rows.
        modelBuilder.Entity<Legality>(legality =>
        {
            legality.ToTable("legality", t => t.HasCheckConstraint(
                "ck_legality_format_version", LegalityFormatVersionCheck));
            // A Brawl row has no banlist version, and a key column cannot be
            // null, so the key holds the generated version_key instead.
            legality.HasKey(l => new { l.CardId, l.Format, l.VersionKey, l.Role }).HasName("pk_legality");
            VersionKeyColumn(legality);
            legality.Property(l => l.LegalityAsOf).HasComment(LegalityRow.LegalityAsOfContract);
            legality.HasOne<Card>().WithMany().HasForeignKey(l => l.CardId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_legality_card");
            legality.HasOne<BanlistVersion>().WithMany().HasForeignKey(l => l.BanlistVersionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_legality_banlist_version");
            legality.HasOne<BanEvent>().WithMany().HasForeignKey(l => l.Reason)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_legality_reason");
            // legal_candidates(banlist_version, format, role) is one seek on
            // this prefix; status is the last key column, so the index also
            // holds the status filter. The format default only backfills the rows that predate
            // the format column; the rebuild writes the format of every row.
            legality.HasIndex(l => new { l.BanlistVersionId, l.Format, l.Role, l.Status })
                .HasDatabaseName("ix_legality_version_format_role");
            legality.Property(l => l.Format)
                .HasConversion(v => WireName(v), s => ParseWireName<Format>(s))
                .HasDefaultValue(Format.Commander);
            legality.Property(l => l.Role)
                .HasConversion(v => WireName(v), s => ParseWireName<Role>(s));
            legality.Property(l => l.Status)
                .HasConversion(v => WireName(v), s => ParseWireName<LegalityStatus>(s));
            legality.HasIndex(l => l.Reason).HasDatabaseName("ix_legality_reason");
        });

        modelBuilder.Entity<LegalityStaging>(legality =>
        {
            legality.ToTable("legality_staging", t => t.HasCheckConstraint(
                "ck_legality_format_version_staging", LegalityFormatVersionCheck));
            legality.HasKey(l => new { l.CardId, l.Format, l.VersionKey, l.Role }).HasName("pk_legality_staging");
            VersionKeyColumn(legality);
            legality.Property(l => l.LegalityAsOf).HasComment(LegalityRow.LegalityAsOfContract);
            legality.HasOne<Card>().WithMany().HasForeignKey(l => l.CardId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_legality_card_staging");
            legality.HasOne<BanlistVersion>().WithMany().HasForeignKey(l => l.BanlistVersionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_legality_banlist_version_staging");
            legality.HasOne<BanEvent>().WithMany().HasForeignKey(l => l.Reason)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_legality_reason_staging");
            legality.HasIndex(l => new { l.BanlistVersionId, l.Format, l.Role, l.Status })
                .HasDatabaseName("ix_legality_version_format_role_staging");
            legality.Property(l => l.Format)
                .HasConversion(v => WireName(v), s => ParseWireName<Format>(s))
                .HasDefaultValue(Format.Commander);
            legality.Property(l => l.Role)
                .HasConversion(v => WireName(v), s => ParseWireName<Role>(s));
            legality.Property(l => l.Status)
                .HasConversion(v => WireName(v), s => ParseWireName<LegalityStatus>(s));
            legality.HasIndex(l => l.Reason).HasDatabaseName("ix_legality_reason_staging");
        });

        // Source profiles are fully derived and rebuilt every ingest, so the
        // card FK cascades like legality's: the same run's rebuild
        // regenerates any rows an upstream correction takes with it.
        modelBuilder.Entity<SourceProfile>(profile =>
        {
            profile.ToTable("source_profile");
            profile.HasKey(p => p.CardId).HasName("pk_source_profile");
            profile.HasOne<Card>().WithMany().HasForeignKey(p => p.CardId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_source_profile_card");
            profile.Property(p => p.FetchLandTypes).HasComment(SourceProfile.FetchLandTypesContract);
            profile.Property(p => p.EntersTapped)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<EntersTappedState>(s))
                .HasComment(SourceProfile.EntersTappedContract);
            profile.Property(p => p.FetchActivation)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<FetchActivation>(s))
                .HasComment(SourceProfile.FetchActivationContract);
            profile.Property(p => p.IdentityBound).HasComment(SourceProfile.IdentityBoundContract);
            profile.Property(p => p.OpponentDependent).HasComment(SourceProfile.OpponentDependentContract);
            profile.Property(p => p.TokenToAnotherPlayer).HasComment(SourceProfile.TokenToAnotherPlayerContract);
        });

        modelBuilder.Entity<SourceProfileStaging>(profile =>
        {
            profile.ToTable("source_profile_staging");
            profile.HasKey(p => p.CardId).HasName("pk_source_profile_staging");
            profile.HasOne<Card>().WithMany().HasForeignKey(p => p.CardId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_source_profile_card_staging");
            profile.Property(p => p.EntersTapped)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<EntersTappedState>(s));
            profile.Property(p => p.FetchActivation)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<FetchActivation>(s));
        });

        // Card feature profiles are fully derived and rebuilt every ingest,
        // so the card FK cascades like the source profile's. The staging
        // twin carries the same column comment: the swap exchanges the
        // tables, so the live table is the old staging table after a run.
        modelBuilder.Entity<CardFeatureProfileRow>(profile =>
        {
            profile.ToTable("card_feature_profile");
            profile.HasKey(p => new { p.CardId, p.FaceIndex }).HasName("pk_card_feature_profile");
            profile.HasOne<Card>().WithMany().HasForeignKey(p => p.CardId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_card_feature_profile_card");
            profile.Property(p => p.TutorDestination)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<TutorDestination>(s));
            profile.Property(p => p.TutorVia)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<TutorVia>(s));
            profile.Property(p => p.Unprofiled).HasComment(CardFeatureProfileRow.UnprofiledContract);
            profile.Property(p => p.ResponseCounterReach)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<CounterReach>(s));
            profile.Property(p => p.ResponseUnprofiled).HasComment(CardFeatureProfileRow.ResponseUnprofiledContract);
        });

        modelBuilder.Entity<CardFeatureProfileRowStaging>(profile =>
        {
            profile.ToTable("card_feature_profile_staging");
            profile.HasKey(p => new { p.CardId, p.FaceIndex }).HasName("pk_card_feature_profile_staging");
            profile.HasOne<Card>().WithMany().HasForeignKey(p => p.CardId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_card_feature_profile_card_staging");
            profile.Property(p => p.TutorDestination)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<TutorDestination>(s));
            profile.Property(p => p.TutorVia)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<TutorVia>(s));
            profile.Property(p => p.Unprofiled).HasComment(CardFeatureProfileRow.UnprofiledContract);
            profile.Property(p => p.ResponseCounterReach)
                .HasConversion(v => WireName(v!.Value), s => ParseWireName<CounterReach>(s));
            profile.Property(p => p.ResponseUnprofiled).HasComment(CardFeatureProfileRow.ResponseUnprofiledContract);
        });

        // Every ingest derives and rebuilds the whole popularity order
        // (ADR 0012), so the card FK cascades like legality's. A candidate
        // query joins the rank on the whole key.
        PopularityRankTable<PopularityRank>(modelBuilder, "");
        PopularityRankTable<PopularityRankStaging>(modelBuilder, "_staging");
        PopularitySourceTable<PopularitySource>(modelBuilder, "");
        PopularitySourceTable<PopularitySourceStaging>(modelBuilder, "_staging");
        CardPopularityTable<CardPopularity>(modelBuilder, "");
        CardPopularityTable<CardPopularityStaging>(modelBuilder, "_staging");

        // The bracket spec data replays from the curated files every ingest
        // (the banlist-events pattern). The spec document is jsonb, not
        // columns: the spec's shape changes often, and those changes stay
        // out of the schema. Restrict on the card FKs: a card hard-delete
        // must not silently shrink a dated snapshot.
        modelBuilder.Entity<BracketSpecVersion>(version =>
        {
            version.ToTable("bracket_spec_version");
            version.HasKey(v => v.Date).HasName("pk_bracket_spec_version");
            version.Property(v => v.Document).HasColumnType("jsonb");
        });

        modelBuilder.Entity<GameChangersSnapshot>(snapshot =>
        {
            snapshot.ToTable("game_changers_snapshot");
            snapshot.HasKey(s => s.Date).HasName("pk_game_changers_snapshot");
        });

        modelBuilder.Entity<GameChangersSnapshotCard>(card =>
        {
            card.ToTable("game_changers_snapshot_card");
            card.HasKey(c => new { c.SnapshotDate, c.CardId }).HasName("pk_game_changers_snapshot_card");
            card.HasOne<GameChangersSnapshot>().WithMany(s => s.Cards).HasForeignKey(c => c.SnapshotDate)
                .HasConstraintName("fk_game_changers_snapshot_card_snapshot");
            card.HasOne(c => c.Card).WithMany().HasForeignKey(c => c.CardId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_game_changers_snapshot_card_card");
            card.HasIndex(c => c.CardId).HasDatabaseName("ix_game_changers_snapshot_card_card_id");
        });

        modelBuilder.Entity<BarometerCard>(card =>
        {
            card.ToTable("barometer_card");
            card.HasKey(c => new { c.Barometer, c.CardId }).HasName("pk_barometer_card");
            card.HasOne(c => c.Card).WithMany().HasForeignKey(c => c.CardId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_barometer_card_card");
            card.HasIndex(c => c.CardId).HasDatabaseName("ix_barometer_card_card_id");
        });

        // The collection FKs target the card's natural key: stored collection
        // state is oracle_id + quantity by design. Cards are marked stale,
        // never deleted, so rows cannot dangle; Restrict makes a hard delete
        // fail loudly anyway.
        modelBuilder.Entity<CollectionCard>(owned =>
        {
            owned.ToTable("collection_card");
            owned.HasKey(c => c.OracleId);
            owned.HasOne<Card>().WithMany().HasForeignKey(c => c.OracleId)
                .HasPrincipalKey(c => c.OracleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CollectionImportRow>(row =>
        {
            row.ToTable("collection_import_row");
            row.HasOne<Card>().WithMany().HasForeignKey(r => r.OracleId)
                .HasPrincipalKey(c => c.OracleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CollectionImportState>(state =>
        {
            SingletonRow(state, "collection_import");
            state.Property(s => s.Format)
                .HasConversion(v => WireName(v), s => ParseWireName<CollectionImportFormat>(s));
        });
    }

    // The live table when suffix is empty, its staging twin when it is
    // "_staging": every name is the live name plus the suffix.
    private static void PopularityRankTable<T>(ModelBuilder modelBuilder, string suffix)
        where T : PopularityRankRow
        => modelBuilder.Entity<T>(rank =>
        {
            rank.ToTable("popularity_rank" + suffix);
            rank.HasKey(r => new { r.CardId, r.Format, r.Order }).HasName("pk_popularity_rank" + suffix);
            rank.HasOne<Card>().WithMany().HasForeignKey(r => r.CardId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_popularity_rank_card" + suffix);
            // The order is total: one card per place.
            rank.HasIndex(r => new { r.Format, r.Order, r.Rank }).IsUnique()
                .HasDatabaseName("ix_popularity_rank_place" + suffix);
            rank.Property(r => r.Format)
                .HasConversion(v => WireName(v), s => ParseWireName<Format>(s));
            rank.Property(r => r.Order)
                .HasConversion(v => WireName(v), s => ParseWireName<PopularityOrder>(s));
        });

    private static void PopularitySourceTable<T>(ModelBuilder modelBuilder, string suffix)
        where T : PopularitySourceRow
        => modelBuilder.Entity<T>(source =>
        {
            source.ToTable("popularity_source" + suffix);
            source.HasKey(s => s.Format).HasName("pk_popularity_source" + suffix);
            source.Property(s => s.Format)
                .HasConversion(v => WireName(v), s => ParseWireName<Format>(s));
            source.Property(s => s.Source)
                .HasConversion(v => WireName(v), s => ParseWireName<PopularitySourceKind>(s));
            source.Property(s => s.AsOf).HasComment(PopularitySourceRow.AsOfContract);
        });

    private static void CardPopularityTable<T>(ModelBuilder modelBuilder, string suffix)
        where T : CardPopularityRow
        => modelBuilder.Entity<T>(popularity =>
        {
            popularity.ToTable("card_popularity" + suffix);
            popularity.HasKey(p => new { p.CardId, p.Format, p.Seat }).HasName("pk_card_popularity" + suffix);
            popularity.HasOne<Card>().WithMany().HasForeignKey(p => p.CardId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_card_popularity_card" + suffix);
            popularity.Property(p => p.Format)
                .HasConversion(v => WireName(v), s => ParseWireName<Format>(s));
            popularity.Property(p => p.Seat)
                .HasConversion(v => WireName(v), s => ParseWireName<Role>(s));
            popularity.Property(p => p.Share).HasPrecision(6, 3).HasComment(CardPopularityRow.ShareContract);
        });

    // Commander rows carry a banlist version; Brawl rows never do (ADR 0012).
    private const string LegalityFormatVersionCheck = "(format = 'commander') = (banlist_version_id IS NOT NULL)";

    // The generated key column of a legality row (LegalityRow.VersionKey).
    // A key cannot be "generated on update", so EF reads it as generated on
    // add: the insert leaves it out and reads it back. The row never moves
    // to another version, so the stored value never changes after insert.
    private static void VersionKeyColumn<T>(EntityTypeBuilder<T> legality) where T : LegalityRow
        => legality.Property(l => l.VersionKey)
            .HasComputedColumnSql("COALESCE(banlist_version_id, 0)", stored: true)
            .ValueGeneratedOnAdd()
            .HasComment(LegalityRow.VersionKeyContract);

    // One row, id = 1, enforced by the database rather than by convention.
    private static void SingletonRow<T>(EntityTypeBuilder<T> entity, string table) where T : class
    {
        entity.ToTable(table, t => t.HasCheckConstraint($"ck_{table}_singleton", "id = 1"));
        entity.Property("Id").ValueGeneratedNever();
    }

    /// <summary>
    /// Multi-word enums store their wire literal verbatim
    /// (<c>commander_only</c>), so the column matches the file format and
    /// the API and never drifts from either.
    /// </summary>
    internal static string WireName<T>(T value) where T : struct, Enum
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private static T ParseWireName<T>(string stored) where T : struct, Enum
        => Enum.Parse<T>(stored.Replace("_", ""), ignoreCase: true);
}
