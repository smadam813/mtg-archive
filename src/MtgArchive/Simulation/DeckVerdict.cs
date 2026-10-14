using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Simulation;

/// <summary>
/// The deck verdict (GLOSSARY.md "Deck verdict", ADR 0012): the service's
/// deterministic judgment of one decklist in one format. It combines card
/// legality, read from the materialized legality table, with deck shape:
/// deck size, copy limit, color identity, commander pairing, and the
/// companion slot.
/// <para>
/// The verdict picks the first status that holds, in this order. <c>illegal</c>: at least one break
/// that more cards cannot fix. <c>unknown</c>: the legality table lacks a
/// row for some card, so the verdict cannot say more. <c>incomplete</c>:
/// more cards can fix every finding (too few cards, no commander).
/// <c>legal</c>: no finding.
/// </para>
/// </summary>
public sealed record DeckVerdict(
    DeckVerdictStatus Status,
    [property: Description("Every card whose status in its role is not legal. A Commander ban cites its ban event; every Brawl finding cites its legality row.")]
    IReadOnlyList<CardFinding> CardFindings,
    [property: Description("Every deck shape rule the list breaks, each citing its comprehensive rule.")]
    IReadOnlyList<ShapeFinding> ShapeFindings,
    [property: Description("The commanders plus the entry quantities. The companion does not count.")]
    int DeckSize,
    [property: Description("The format's deck size: 100, or 60 in Standard Brawl.")]
    int RequiredDeckSize)
{
    /// <summary>The versions the verdict read: set by the deck verdict
    /// route, null where an envelope carries its own block.</summary>
    public VersionsBlock? Versions { get; init; }

    [System.ComponentModel.Description("The A- names that resolved only without the prefix, each with the card it resolved to; absent when there is none.")]
    public IReadOnlyList<NameRewrite>? NameRewrites { get; init; }

    public static int RequiredSize(Format format) => format == Format.StandardBrawl ? 60 : 100;

    /// <summary>Judges the resolved decklist in the format. A Commander
    /// verdict reads the rows at <paramref name="pin"/>; with no pin no
    /// rows exist and the verdict is <see cref="DeckVerdictStatus.Unknown"/>.
    /// A Brawl verdict reads the format's one current state.</summary>
    public static async Task<DeckVerdict> ComputeAsync(
        ArchiveDbContext db, ResolvedDecklist decklist, Format format, BanlistVersionPin? pin, CancellationToken cancellationToken)
    {
        var ids = decklist.Commanders
            .Concat(decklist.Companion is { } companionId ? [companionId] : [])
            .Concat(decklist.Entries.Select(e => e.OracleId))
            .Distinct()
            .ToList();
        var cards = (await db.Cards
            .Where(c => ids.Contains(c.OracleId))
            .Select(c => new VerdictCard(
                c.Id, c.OracleId, c.Name, c.ColorIdentityMask, c.CopyLimit, c.Supertypes, c.CardTypes, c.Subtypes,
                c.OracleText, c.HasPartner, c.HasPartnerWith, c.HasFriendsForever, c.HasBackground, c.HasDoctorsCompanion))
            .ToListAsync(cancellationToken))
            .ToDictionary(c => c.OracleId);

        var versionId = FormatParameter.IsVersioned(format) ? pin?.Id : null;
        List<VerdictRow> rows = [];
        if (!FormatParameter.IsVersioned(format) || pin is not null)
        {
            var read = await db.Legalities
                .Where(l => l.Format == format && l.BanlistVersionId == versionId)
                .Join(db.Cards, l => l.CardId, c => c.Id, (l, c) => new { Legality = l, c.OracleId })
                .Where(x => ids.Contains(x.OracleId))
                .Select(x => new
                {
                    x.OracleId,
                    x.Legality.Role,
                    x.Legality.Status,
                    x.Legality.ScryfallKey,
                    x.Legality.LegalityAsOf,
                    Reason = BanReasons.Query(db).FirstOrDefault(r => r.Id == x.Legality.Reason),
                })
                .ToListAsync(cancellationToken);
            rows = [.. read.Select(x => new VerdictRow(x.OracleId, x.Role, x.Status, x.ScryfallKey, x.LegalityAsOf, x.Reason))];
        }

        var rowOf = rows.ToDictionary(r => (r.OracleId, r.Role));

        var commanders = decklist.Commanders.Select(id => cards[id]).ToList();
        var shape = new List<ShapeFinding>();
        var size = commanders.Count + decklist.DeckSize;
        var required = RequiredSize(format);
        if (size < required)
        {
            shape.Add(new ShapeFinding(
                ShapeFindingKind.TooFewCards, $"The deck has {size} cards; the format needs {required}.", [], SizeRule(format)));
        }
        else if (size > required)
        {
            shape.Add(new ShapeFinding(
                ShapeFindingKind.TooManyCards, $"The deck has {size} cards; the format allows {required}.", [], SizeRule(format)));
        }

        if (commanders.Count == 0)
        {
            // CR 903.3: a deck has a legendary card designated as its commander.
            shape.Add(new ShapeFinding(ShapeFindingKind.NoCommander, "The deck names no commander.", [], Rule("903.3")));
        }

        else if (commanders.Count > 2 || (commanders.Count == 2 && !IsPair(commanders[0], commanders[1])))
        {
            // CR 702.124: two commanders only as a partner pair, a
            // Background pair, or a Doctor's companion pair.
            shape.Add(new ShapeFinding(
                ShapeFindingKind.Pairing,
                commanders.Count > 2
                    ? $"The deck names {commanders.Count} commanders; at most two can pair."
                    : "The two commanders share no pairing ability.",
                [.. commanders.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => Citation.OfCard(c.OracleId, c.Name))],
                Rule("702.124")));
        }

        // CR 903.5b: the copy limit. A commander is one copy and can never
        // be an entry too (DecklistResolver refuses it).
        foreach (var entry in decklist.Entries.GroupBy(e => e.OracleId).OrderBy(g => cards[g.Key].Name, StringComparer.Ordinal))
        {
            var card = cards[entry.Key];
            var copies = entry.Sum(e => e.Quantity);
            if (card.CopyLimit is { } limit && copies > limit)
            {
                shape.Add(new ShapeFinding(
                    ShapeFindingKind.CopyLimit,
                    $"The deck has {copies} copies of {card.Name}; the limit is {limit}.",
                    [Citation.OfCard(card.OracleId, card.Name)],
                    Rule("903.5b")));
            }
        }

        if (commanders.Count > 0)
        {
            shape.AddRange(ColorIdentityFindings(format, commanders, decklist, cards));
        }

        // Each seat reads the row for its role. A Background in a valid
        // Background pair sits in the command zone by the pairing rule, not
        // by the commander rule, so its pool status comes from the main role.
        var pairedBackground = commanders.Count == 2 && ChoosesBackground(commanders[0], commanders[1])
            ? commanders[1]
            : commanders.Count == 2 && ChoosesBackground(commanders[1], commanders[0]) ? commanders[0] : null;
        var seats = commanders.Select(c => (Card: c, Role: Role.Commander, RowRole: c == pairedBackground ? Role.Main : Role.Commander))
            .Concat(decklist.Companion is { } companion ? [(cards[companion], Role.Companion, Role.Companion)] : [])
            .Concat(decklist.Entries.Select(e => e.OracleId).Distinct().Select(id => (Card: cards[id], Role: Role.Main, RowRole: Role.Main)))
            .ToList();
        var missingRows = false;
        var cardFindings = new List<CardFinding>();
        foreach (var (card, role, rowRole) in seats)
        {
            if (!rowOf.TryGetValue((card.OracleId, rowRole), out var row))
            {
                missingRows = true;
                continue;
            }

            if (row.Status != LegalityStatus.Legal)
            {
                cardFindings.Add(new CardFinding(
                    Citation.OfCard(card.OracleId, card.Name),
                    role,
                    row.Status,
                    row.Reason is null ? null : BanlistEndpoints.EventCitation(row.Reason, card.Name),
                    LegalityCitation.OfRow(
                        card.OracleId,
                        row.ScryfallKey,
                        row.LegalityAsOf,
                        rowOf.TryGetValue((card.OracleId, Role.Main), out var main) ? main.Status : row.Status,
                        row.Role,
                        row.Status)));
            }
        }

        var status = cardFindings.Count > 0 || shape.Any(f => !IsFixable(f.Kind)) ? DeckVerdictStatus.Illegal
            : missingRows ? DeckVerdictStatus.Unknown
            : shape.Count > 0 ? DeckVerdictStatus.Incomplete
            : DeckVerdictStatus.Legal;
        return new DeckVerdict(
            status,
            [.. cardFindings.OrderBy(f => f.Role).ThenBy(f => f.Card.Label, StringComparer.Ordinal)],
            shape,
            size,
            required);
    }

    /// <summary>Whether more cards can fix the finding.</summary>
    public static bool IsFixable(ShapeFindingKind kind) => kind is ShapeFindingKind.TooFewCards or ShapeFindingKind.NoCommander;

    private static Citation Rule(string number) => new(CitationType.Rule, number, number);

    // CR 903.5a: a Commander deck is exactly 100 cards. CR 903.12: the
    // Brawl rules, which set 60 for Standard Brawl. Brawl and Competitive
    // Brawl keep the 100-card size of the Commander rule on Arena.
    private static Citation SizeRule(Format format) => Rule(format == Format.StandardBrawl ? "903.12" : "903.5a");

    private static readonly string[] BasicLandTypes = ["Plains", "Island", "Swamp", "Mountain", "Forest"];

    /// <summary>
    /// CR 903.5c: every card and the companion lie within the commanders'
    /// color identity. CR 903.12e: in a Brawl format a colorless commander
    /// allows any number of basic lands of one basic land type. The verdict
    /// reads the type from the list, so two or more types is a break.
    /// </summary>
    private static IEnumerable<ShapeFinding> ColorIdentityFindings(
        Format format, List<VerdictCard> commanders, ResolvedDecklist decklist, Dictionary<Guid, VerdictCard> cards)
    {
        var identity = commanders.Aggregate(0, (mask, c) => mask | c.ColorIdentityMask);
        var members = decklist.Entries.Select(e => e.OracleId).Distinct().Select(id => cards[id])
            .Concat(decklist.Companion is { } companion ? [cards[companion]] : [])
            .ToList();
        var offIdentity = members.Where(c => (c.ColorIdentityMask & ~identity) != 0).ToList();
        if (format != Format.Commander && identity == 0)
        {
            var basics = offIdentity
                .Where(c => c.Supertypes.Contains("Basic") && c.CardTypes.Contains("Land") && c.Subtypes.Intersect(BasicLandTypes).Any())
                .ToList();
            offIdentity = [.. offIdentity.Except(basics)];
            var types = basics.SelectMany(c => c.Subtypes.Intersect(BasicLandTypes)).Distinct().ToList();
            if (types.Count > 1)
            {
                yield return new ShapeFinding(
                    ShapeFindingKind.BasicLandTypes,
                    $"A colorless commander allows basic lands of one basic land type; the deck has {types.Count}: {string.Join(", ", types)}.",
                    [.. basics.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => Citation.OfCard(c.OracleId, c.Name))],
                    Rule("903.12e"));
            }
        }

        if (offIdentity.Count > 0)
        {
            yield return new ShapeFinding(
                ShapeFindingKind.ColorIdentity,
                $"{offIdentity.Count} card(s) lie outside the commander's color identity.",
                [.. offIdentity.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => Citation.OfCard(c.OracleId, c.Name))],
                Rule("903.5c"));
        }
    }

    /// <summary>The CR pairing rules (702.124), in all four formats.</summary>
    private static bool IsPair(VerdictCard a, VerdictCard b)
        => (a.HasPartner && b.HasPartner)
            || (a.HasPartnerWith && b.HasPartnerWith && NamesPartner(a, b) && NamesPartner(b, a))
            || (a.HasFriendsForever && b.HasFriendsForever)
            || ChoosesBackground(a, b) || ChoosesBackground(b, a)
            || (a.HasDoctorsCompanion && IsDoctor(b)) || (b.HasDoctorsCompanion && IsDoctor(a));

    private static bool NamesPartner(VerdictCard card, VerdictCard partner)
        => card.OracleText?.Contains($"Partner with {partner.Name}", StringComparison.Ordinal) == true;

    private static bool IsDoctor(VerdictCard card) => card.Subtypes.Contains("Time Lord") && card.Subtypes.Contains("Doctor");

    private static bool IsBackground(VerdictCard card) => card.Subtypes.Contains("Background");

    private static bool ChoosesBackground(VerdictCard commander, VerdictCard background)
        => commander.HasBackground && !IsBackground(commander) && IsBackground(background);

    private sealed record VerdictCard(
        int Id,
        Guid OracleId,
        string Name,
        int ColorIdentityMask,
        int? CopyLimit,
        string[] Supertypes,
        string[] CardTypes,
        string[] Subtypes,
        string? OracleText,
        bool HasPartner,
        bool HasPartnerWith,
        bool HasFriendsForever,
        bool HasBackground,
        bool HasDoctorsCompanion);

    private sealed record VerdictRow(
        Guid OracleId, Role Role, LegalityStatus Status, string? ScryfallKey, DateTimeOffset? LegalityAsOf, BanReason? Reason);
}

/// <summary>Legal: no finding. Incomplete: more cards can fix every
/// finding. Illegal: at least one break more cards cannot fix. Unknown:
/// the legality table lacks a row for some card.</summary>
public enum DeckVerdictStatus
{
    Legal,
    Incomplete,
    Illegal,
    Unknown,
}

/// <summary>One card whose status in its role is not legal. A Commander
/// ban cites its ban event in <see cref="BanEvent"/>; a Brawl finding cites
/// its legality row in <see cref="Citation"/>, whatever the status.</summary>
public sealed record CardFinding(
    Citation Card,
    Role Role,
    LegalityStatus Status,
    [property: Description("The ban event a Commander banned status cites; absent otherwise.")]
    Citation? BanEvent,
    [property: Description("The legality citation of a Brawl format's row; absent in Commander.")]
    Citation? Citation);

/// <summary>One deck shape rule the list breaks: its kind, a sentence with
/// the numbers, the cards it names, and the comprehensive rule it
/// cites.</summary>
public sealed record ShapeFinding(
    [property: Description("too_few_cards and no_commander are fixable by more cards; every other kind is a break.")]
    ShapeFindingKind Kind,
    string Detail,
    [property: Description("The cards the finding names; empty for a deck size finding.")]
    IReadOnlyList<Citation> Cards,
    [property: Description("The comprehensive rule the finding cites.")]
    Citation Citation);

public enum ShapeFindingKind
{
    TooFewCards,
    NoCommander,
    TooManyCards,
    Pairing,
    CopyLimit,
    ColorIdentity,
    BasicLandTypes,
}
