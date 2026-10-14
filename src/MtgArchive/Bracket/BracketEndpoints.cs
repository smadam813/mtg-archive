using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;
using MtgArchive.Simulation;

namespace MtgArchive.Bracket;

/// <summary>
/// POST /bracket-classification resolves the decklist and both version
/// pins, then loads the barometer sets for the pinned spec and snapshot.
/// It returns the classifier's four parts, the versions block, and the two
/// snapshot citations. The reference endpoints make those citations
/// fetchable: GET /bracket-spec/versions and /bracket-spec/{date} serve
/// the spec versions, GET /game-changers/versions and /game-changers/{date}
/// the snapshots. No legality code path runs here.
/// </summary>
public sealed class BracketEndpoints : IEndpointMapper
{
    public const string SpecParameter = "bracket_spec_version";
    public const string SnapshotParameter = "gc_snapshot";

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/bracket-classification", async Task<Results<Ok<BracketClassificationResponse>, ProblemHttpResult>> (
            BracketClassificationRequest request,
            ArchiveDbContext db,
            BracketVersionAxis axis,
            CancellationToken cancellationToken) =>
        {
            var format = FormatParameter.Resolve(request.Format);
            if (format != Format.Commander)
            {
                return TypedResults.Problem(Problems.CommanderOnly(FormatParameter.WireName(format)));
            }

            if (request.TargetBracket is not { } target)
            {
                return TypedResults.Problem(Problems.MissingParameter("target_bracket"));
            }

            var spec = await axis.ResolveSpecAsync(db, request.BracketSpecVersion, cancellationToken);
            var snapshotDate = await axis.ResolveSnapshotAsync(db, request.GcSnapshot, cancellationToken);
            if (spec is null)
            {
                return TypedResults.Problem(Problems.NothingIngested(SpecParameter));
            }

            if (snapshotDate is null)
            {
                return TypedResults.Problem(Problems.NothingIngested(SnapshotParameter));
            }

            if (!spec.Brackets.Contains(target))
            {
                return TypedResults.Problem(Problems.UnknownBracket("target_bracket", target, spec.Brackets));
            }

            var assumptions = request.Assumptions ?? BracketAssumptions.None;
            if (assumptions.IntentBracket is { } intent && !spec.Brackets.Contains(intent))
            {
                return TypedResults.Problem(Problems.UnknownBracket("assumptions.intent_bracket", intent, spec.Brackets));
            }

            ResolvedDecklist decklist;
            try
            {
                decklist = await new DecklistResolver(db).ResolveAsync(request.Decklist, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                return TypedResults.Problem(Problems.InvalidDecklist(ex.Message));
            }

            if (decklist.Unresolved.Count > 0)
            {
                return TypedResults.Problem(Problems.UnresolvedDecklist(decklist.Unresolved));
            }

            var ids = decklist.Commanders.Concat(decklist.Entries.Select(e => e.OracleId)).Distinct().ToList();
            var names = await db.Cards
                .Where(c => ids.Contains(c.OracleId))
                .Select(c => new { c.OracleId, c.Name })
                .ToDictionaryAsync(c => c.OracleId, c => c.Name, cancellationToken);
            var deck = ids.Select(id => new ClassifiedCard(id, names[id])).ToList();

            var gameChangers = await db.GameChangersSnapshots
                .Where(s => s.Date == snapshotDate)
                .SelectMany(s => s.Cards)
                .Where(c => ids.Contains(c.Card.OracleId))
                .Select(c => c.Card.OracleId)
                .ToListAsync(cancellationToken);
            var listed = await db.BarometerCards
                .Where(c => ids.Contains(c.Card.OracleId))
                .Select(c => new { c.Barometer, c.Card.OracleId })
                .ToListAsync(cancellationToken);
            var sets = new BarometerSets(
                gameChangers.ToHashSet(),
                listed.Where(c => c.Barometer == BracketSpec.MassLandDenial).Select(c => c.OracleId).ToHashSet(),
                listed.Where(c => c.Barometer == BracketSpec.ChainedExtraTurns).Select(c => c.OracleId).ToHashSet());

            var classification = BracketClassifier.Classify(spec, deck, sets, target, assumptions);
            return TypedResults.Ok(new BracketClassificationResponse(
                classification.BracketFloor,
                classification.AssumedBracket,
                classification.Reasons,
                classification.Assumptions,
                [SpecCitation(spec.Date), SnapshotCitation(snapshotDate.Value)],
                new VersionsBlock
                {
                    Format = FormatParameter.WireName(format),
                    BracketSpecVersion = Wire(spec.Date),
                    GcSnapshot = Wire(snapshotDate.Value),
                })
            {
                NameRewrites = decklist.WireRewrites,
            });
        });

        app.MapGet("/bracket-spec/versions", async Task<Ok<DatedVersionsResponse>> (
            ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            var versions = await db.BracketSpecVersions
                .OrderBy(v => v.Date)
                .Select(v => new DatedVersionSummary(v.Date, v.Source))
                .ToListAsync(cancellationToken);
            return TypedResults.Ok(new DatedVersionsResponse(versions));
        });

        app.MapGet("/bracket-spec/{date}", async Task<Results<Ok<BracketSpecDetail>, ProblemHttpResult>> (
            string date, ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            var row = TryParse(date, out var parsed)
                ? await db.BracketSpecVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Date == parsed, cancellationToken)
                : null;
            if (row is null)
            {
                return TypedResults.Problem(Problems.UnknownSnapshot("bracket-spec", date));
            }

            using var document = JsonDocument.Parse(row.Document);
            return TypedResults.Ok(new BracketSpecDetail(
                row.Date, row.Source, SpecCitation(row.Date), document.RootElement.Clone()));
        });

        app.MapGet("/game-changers/versions", async Task<Ok<DatedVersionsResponse>> (
            ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            var versions = await db.GameChangersSnapshots
                .OrderBy(s => s.Date)
                .Select(s => new DatedVersionSummary(s.Date, s.Source))
                .ToListAsync(cancellationToken);
            return TypedResults.Ok(new DatedVersionsResponse(versions));
        });

        app.MapGet("/game-changers/{date}", async Task<Results<Ok<GameChangersSnapshotDetail>, ProblemHttpResult>> (
            string date, ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            var row = TryParse(date, out var parsed)
                ? await db.GameChangersSnapshots
                    .Include(s => s.Cards).ThenInclude(c => c.Card)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(s => s.Date == parsed, cancellationToken)
                : null;
            if (row is null)
            {
                return TypedResults.Problem(Problems.UnknownSnapshot("game-changers", date));
            }

            return TypedResults.Ok(new GameChangersSnapshotDetail(
                row.Date,
                row.Source,
                SnapshotCitation(row.Date),
                [.. row.Cards.OrderBy(c => c.Card.Name, StringComparer.Ordinal).Select(c => Citation.OfCard(c.Card))]));
        });
    }

    /// <summary>The one <c>{ type: snapshot }</c> shape for a spec revision;
    /// the id is the path under GET /.</summary>
    public static Citation SpecCitation(DateOnly date)
        => new(CitationType.Snapshot, $"bracket-spec/{Wire(date)}", $"Commander Brackets spec {Wire(date)}");

    /// <summary>The one <c>{ type: snapshot }</c> shape for a Game Changers snapshot.</summary>
    public static Citation SnapshotCitation(DateOnly date)
        => new(CitationType.Snapshot, $"game-changers/{Wire(date)}", $"Game Changers snapshot {Wire(date)}");

    private static string Wire(DateOnly date) => date.ToString("O", CultureInfo.InvariantCulture);

    private static bool TryParse(string text, out DateOnly date)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>
/// POST /bracket-classification body: the decklist, the numeric target
/// bracket (phrase-to-bracket mapping is skill-side; the service never sees
/// a power-level phrase), optional version pins (latest by default), and
/// the optional declared assumptions.
/// </summary>
public sealed record BracketClassificationRequest
{
    public required Decklist Decklist { get; init; }

    public int? TargetBracket { get; init; }

    public string? BracketSpecVersion { get; init; }

    public string? GcSnapshot { get; init; }

    public BracketAssumptions? Assumptions { get; init; }

    [System.ComponentModel.Description("The format: commander (the default) only. Brackets apply to Commander only, so a Brawl format is a 400 (ADR 0012).")]
    public string? Format { get; init; }
}

/// <summary>The four classifier parts, the snapshot citations, and the versions echo.</summary>
public sealed record BracketClassificationResponse(
    BracketFloor BracketFloor,
    AssumedBracket AssumedBracket,
    IReadOnlyList<BarometerReason> Reasons,
    IReadOnlyList<DeclaredAssumption> Assumptions,
    IReadOnlyList<Citation> Citations,
    VersionsBlock Versions)
{
    [System.ComponentModel.Description("The A- names that resolved only without the prefix, each with the card it resolved to; absent when there is none.")]
    public IReadOnlyList<NameRewrite>? NameRewrites { get; init; }
}

public sealed record DatedVersionsResponse(IReadOnlyList<DatedVersionSummary> Versions);

public sealed record DatedVersionSummary(DateOnly Date, string Source);

/// <summary>GET /bracket-spec/{date}: the revision's file verbatim under <c>document</c>.</summary>
public sealed record BracketSpecDetail(DateOnly Date, string Source, Citation Citation, JsonElement Document);

/// <summary>GET /game-changers/{date}: the snapshot's cards as card citations.</summary>
public sealed record GameChangersSnapshotDetail(
    DateOnly Date, string Source, Citation Citation, IReadOnlyList<Citation> Cards);
