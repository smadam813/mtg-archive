using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Cards;
using MtgArchive.Data;
using MtgArchive.Ingest;

namespace MtgArchive.Banlist;

/// <summary>
/// Replays the curated events file into the derived <c>banlist_version</c>
/// and <c>ban_event</c> tables (ADR 0004: the file is banlist truth — never
/// Scryfall <c>legalities.commander</c>, and never auto-created events from
/// snapshot diffs). The curated categories file joins it (ADR 0010): each
/// category expands into one ban event per member card, dated by the
/// announcement that put the category in force, under the same
/// version as a named announcement on that date. The replay is a diff
/// against the current rows keyed by effective date and card, so re-runs
/// and in-place corrections keep every row's id — ban-event citations stay
/// stable; an id retires only when its card leaves the announcement or the
/// announcement leaves the files. Cards resolve against the corpus the card
/// upsert phase just refreshed; any unresolvable name, empty category, or
/// date collision fails the run with every problem itemized. One
/// SaveChanges commits the phase.
/// </summary>
public sealed class BanlistReplayPhase(ArchiveDbContext db, IConfiguration configuration, BanlistVersionAxis axis) : IIngestPhase
{
    public string Name => IngestPhases.BanlistReplay;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var path = configuration["Banlist:EventsPath"] ?? BanlistEventsFile.DefaultPath;
        var categoriesPath = configuration["Banlist:CategoriesPath"] ?? BanlistCategoriesFile.DefaultPath;
        var errors = new List<string>();
        var files = BanlistEventsFile.Read(path, errors);
        var categories = BanlistCategoriesFile.Read(categoriesPath, errors);
        if (errors.Count > 0)
        {
            throw new IngestValidationException(errors);
        }

        var versions = await ExpandAsync(files, categories, cancellationToken);
        var existing = await db.BanlistVersions
            .Include(v => v.Events)
            .ToDictionaryAsync(v => v.EffectiveDate, cancellationToken);

        foreach (var version in versions)
        {
            if (!existing.Remove(version.EffectiveDate, out var row))
            {
                row = new BanlistVersion
                {
                    EffectiveDate = version.EffectiveDate,
                    DatePrecision = version.DatePrecision,
                    AnnouncementUrl = version.AnnouncementUrl,
                };
                db.BanlistVersions.Add(row);
            }

            row.DatePrecision = version.DatePrecision;
            row.AnnouncementUrl = version.AnnouncementUrl;
            ApplyEvents(row, version);
        }

        // Versions the file no longer contains are derived rows to drop.
        db.BanlistVersions.RemoveRange(existing.Values);

        await db.SaveChangesAsync(cancellationToken);
        axis.Invalidate();
        try
        {
            await DetectDriftAsync(context, path, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The detector is a report-only diagnostic: its failure lands in
            // the report's errors and never fails the phase whose replay
            // already committed.
            context.Report.Errors.Add($"banlist drift detection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The banlist drift detector (ADR 0004): a snapshot card whose
    /// <c>legalities.commander</c> disagrees with the standing ban state the
    /// files just replayed lands in the report's drift detections — report
    /// only; the diff never writes ban events, and resolution is a human
    /// appending the dated event and re-ingesting. A commander_only or
    /// companion ban keeps the card legal in the deck, so Scryfall isn't
    /// expected to say banned for it (Lutri since 2026-02-09).
    /// </summary>
    private async Task DetectDriftAsync(IngestContext context, string path, CancellationToken cancellationToken)
    {
        if (context.Snapshot is not { } snapshot)
        {
            return;
        }

        var history = await db.BanlistVersions
            .Include(v => v.Events)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var standing = BanState.Replay(history)
            .Where(e => e.Scope == BanScope.All)
            .Select(e => e.CardId)
            .ToHashSet();
        var bannedOracleIds = (await db.Cards
                .Where(c => standing.Contains(c.Id))
                .Select(c => c.OracleId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var card in snapshot.Cards.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            // A not_legal card never enters a deck, so a ban on it changes
            // nothing: category rules may match one (a playtest Conspiracy).
            if (card.CommanderLegality is not { } legality || legality == "not_legal")
            {
                continue;
            }

            var scryfallBanned = legality == "banned";
            var curatedBanned = bannedOracleIds.Contains(card.OracleId);
            if (scryfallBanned == curatedBanned)
            {
                continue;
            }

            context.Report.DriftDetections.Add(JsonSerializer.SerializeToElement(
                new BanlistDriftDetection(
                    Kind: "banlist",
                    OracleId: card.OracleId,
                    Name: card.Name,
                    ScryfallCommanderLegality: legality,
                    CuratedBanned: curatedBanned,
                    Resolution: $"report-only: append the dated event with announcement provenance to {path} and re-ingest"),
                WireJson.Options));
        }
    }

    /// <summary>
    /// The replay's desired state: every events-file version, plus each
    /// category's member cards as <c>ban</c>/<c>all</c> events at the
    /// category's effective date. A category on a date the events file
    /// already holds joins that version, so the date must carry the same
    /// announcement; a card changes at most once per date.
    /// </summary>
    private async Task<IReadOnlyList<ReplayVersion>> ExpandAsync(
        IReadOnlyList<BanlistFileVersion> files,
        IReadOnlyList<BanlistFileCategory> categories,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var named = await ResolveNamesAsync(
            "banlist-events", files.SelectMany(v => v.Events).Select(e => e.Card), errors, cancellationToken);
        var listed = await ResolveNamesAsync(
            "banlist-categories",
            categories.Select(c => c.Members).OfType<CategoryMembers.Cards>().SelectMany(m => m.Names),
            errors, cancellationToken);
        if (errors.Count > 0)
        {
            throw new IngestValidationException(errors);
        }

        var versions = files.ToDictionary(v => v.EffectiveDate, v => new ReplayVersion(
            v.EffectiveDate, v.DatePrecision, v.AnnouncementUrl,
            [.. v.Events.Select(e => new ReplayEvent(named[e.Card], e.Card, e.Action, e.Scope, null))]));
        foreach (var category in categories)
        {
            var where = $"banlist-categories '{category.Key}'";
            if (!versions.TryGetValue(category.EffectiveDate, out var version))
            {
                version = new ReplayVersion(category.EffectiveDate, category.DatePrecision, category.AnnouncementUrl, []);
                versions.Add(category.EffectiveDate, version);
            }
            else if (version.AnnouncementUrl != category.AnnouncementUrl || version.DatePrecision != category.DatePrecision)
            {
                errors.Add($"{where}: {category.EffectiveDate:O} already cites {version.AnnouncementUrl} "
                    + $"({ArchiveDbContext.WireName(version.DatePrecision)} precision) — one announcement per date");
                continue;
            }

            var members = await MembersAsync(category.Members, listed, cancellationToken);
            if (members.Count == 0)
            {
                errors.Add($"{where}: the member rule matches no card");
            }

            foreach (var (cardId, name) in members)
            {
                if (version.Events.Any(e => e.CardId == cardId))
                {
                    errors.Add($"{where}: '{name}' already changes on {category.EffectiveDate:O} — one event per card per announcement");
                    continue;
                }

                version.Events.Add(new ReplayEvent(cardId, name, BanAction.Ban, BanScope.All, category.Key));
            }
        }

        return errors.Count == 0 ? [.. versions.Values] : throw new IngestValidationException(errors);
    }

    /// <summary>
    /// A category's member cards, by name. <c>oracle_word</c> narrows in SQL
    /// with ILIKE and settles the whole-word match here; the word is letters
    /// only (the parser checks), so neither pattern needs escaping.
    /// </summary>
    private async Task<List<(int Id, string Name)>> MembersAsync(
        CategoryMembers members, Dictionary<string, int> listed, CancellationToken cancellationToken)
    {
        switch (members)
        {
            case CategoryMembers.CardType(var type):
                return [.. (await db.Cards
                        .Where(c => c.CardTypes.Contains(type))
                        .Select(c => new { c.Id, c.Name })
                        .ToListAsync(cancellationToken))
                    .OrderBy(c => c.Name, StringComparer.Ordinal)
                    .Select(c => (c.Id, c.Name))];
            case CategoryMembers.OracleWord(var word):
                var like = $"%{word}%";
                var wholeWord = new Regex($@"\b{word}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rows = await db.Cards
                    .Where(c => EF.Functions.ILike(c.OracleText!, like)
                        || c.Faces.Any(f => EF.Functions.ILike(f.OracleText!, like)))
                    .Select(c => new { c.Id, c.Name, c.OracleText, Faces = c.Faces.Select(f => f.OracleText).ToList() })
                    .ToListAsync(cancellationToken);
                return [.. rows
                    .Where(c => c.Faces.Prepend(c.OracleText).Any(text => text is not null && wholeWord.IsMatch(text)))
                    .OrderBy(c => c.Name, StringComparer.Ordinal)
                    .Select(c => (c.Id, c.Name))];
            case CategoryMembers.Cards(var names):
                return [.. names.Select(n => (listed[n], n))];
            default:
                throw new ArgumentOutOfRangeException(nameof(members), members, null);
        }
    }

    /// <summary>
    /// Exact-Oracle-name resolution under <see cref="NamesakePrecedence"/>.
    /// A name that matches nothing, or namesakes that survive the rule, land
    /// in <paramref name="errors"/>. The failure is never a warning, because
    /// these rows are dated citation.
    /// </summary>
    private async Task<Dictionary<string, int>> ResolveNamesAsync(
        string source, IEnumerable<string> referenced, List<string> errors, CancellationToken cancellationToken)
    {
        var names = referenced.Distinct(StringComparer.Ordinal).ToList();
        var rows = await db.Cards
            .Where(c => names.Contains(c.Name))
            .Select(c => new { c.Id, c.Name, c.Stale, c.Layout })
            .ToListAsync(cancellationToken);
        var byName = rows.ToLookup(r => r.Name, StringComparer.Ordinal);

        var resolved = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var candidates = NamesakePrecedence.Candidates(byName[name].ToList(), c => c.Stale, c => c.Layout);
            switch (candidates.Count)
            {
                case 0:
                    errors.Add($"{source}: no card named '{name}' — the reference is exact Oracle name");
                    break;
                case 1:
                    resolved[name] = candidates[0].Id;
                    break;
                default:
                    errors.Add($"{source}: '{name}' matches {candidates.Count} cards — the corpus needs repair");
                    break;
            }
        }

        return resolved;
    }

    /// <summary>
    /// An event's identity within an announcement is its card (the unique
    /// index agrees), so a transcription correction to action, scope, or
    /// category updates the row in place and the citation id survives; an
    /// id retires only when its card leaves the announcement.
    /// </summary>
    private static void ApplyEvents(BanlistVersion row, ReplayVersion version)
    {
        var desired = version.Events.ToDictionary(e => e.CardId);
        row.Events.RemoveAll(e => !desired.ContainsKey(e.CardId));
        foreach (var banEvent in row.Events)
        {
            var wanted = desired[banEvent.CardId];
            banEvent.Action = wanted.Action;
            banEvent.Scope = wanted.Scope;
            banEvent.Category = wanted.Category;
        }

        var current = row.Events.Select(e => e.CardId).ToHashSet();
        row.Events.AddRange(version.Events
            .Where(e => !current.Contains(e.CardId))
            .Select(e => new BanEvent { CardId = e.CardId, Action = e.Action, Scope = e.Scope, Category = e.Category }));
    }

    /// <summary>One version of the replay's desired state.</summary>
    private sealed record ReplayVersion(
        DateOnly EffectiveDate, DatePrecision DatePrecision, string AnnouncementUrl, List<ReplayEvent> Events);

    /// <summary>One desired event; <see cref="Category"/> is null for a named one.</summary>
    private sealed record ReplayEvent(int CardId, string Name, BanAction Action, BanScope Scope, string? Category);
}

/// <summary>One drift detection as reported (snake_case in the ingest
/// report's <c>drift_detections</c>); <c>kind</c> discriminates it from the
/// Game Changers and pinned-text drifts.</summary>
public sealed record BanlistDriftDetection(
    string Kind,
    Guid OracleId,
    string Name,
    string ScryfallCommanderLegality,
    bool CuratedBanned,
    string Resolution);
