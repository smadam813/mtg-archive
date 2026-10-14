using System.Globalization;

namespace MtgArchive.Ingest;

/// <summary>
/// Everything the corpus derives from a Scryfall record at ingest, never at
/// query time: the parsed type line, front-face mana value, commander
/// eligibility, the partner-mechanic flags, and the copy limit. Pure
/// functions over <see cref="SnapshotCard"/> so the rules are unit-testable
/// against fixture records.
/// </summary>
public static class CardDerivations
{
    public sealed record Derived(
        string[] Supertypes,
        string[] CardTypes,
        string[] Subtypes,
        decimal ManaValue,
        bool CanBeCommander,
        bool CanBeBrawlCommander,
        bool HasPartner,
        bool HasPartnerWith,
        bool HasFriendsForever,
        bool HasBackground,
        bool HasDoctorsCompanion,
        int? CopyLimit,
        bool DigitalOnly);

    public static Derived Derive(SnapshotCard card)
    {
        var (supertypes, cardTypes, subtypes) = ParseTypeLine(card.TypeLine);
        var text = CombinedOracleText(card);
        var hasPartnerWith = card.Keywords.Contains("Partner with");
        return new Derived(
            supertypes,
            cardTypes,
            subtypes,
            ManaValue(card),
            CanBeCommander(card, text),
            CanBeBrawlCommander(card, text),
            // Scryfall lists the named Partner—variants (Friends forever,
            // Survivors, …) under the plain "Partner" keyword; only the text
            // separates them, and Partner With cards carry both keywords.
            HasPartner: card.Keywords.Contains("Partner") && !hasPartnerWith && !text.Contains("Partner—"),
            HasPartnerWith: hasPartnerWith,
            HasFriendsForever: text.Contains("Friends forever"),
            HasBackground: subtypes.Contains("Background")
                || card.Keywords.Contains("Choose a background", StringComparer.OrdinalIgnoreCase)
                || text.Contains("Choose a Background", StringComparison.OrdinalIgnoreCase),
            HasDoctorsCompanion: card.Keywords.Contains("Doctor's companion"),
            CopyLimit: CopyLimit(card, supertypes, text),
            DigitalOnly: DigitalOnly(card.Digital, card.CommanderLegality));
    }

    /// <summary>See <see cref="Data.Card.DigitalOnlyContract"/>. Scryfall
    /// <c>digital</c> alone describes the stored printing. A paper card with
    /// an MTGO reprint as its stored printing reads true, but Commander allows
    /// it. The raw Scryfall <c>legalities.commander</c> value decides, never
    /// the curated banlist.</summary>
    public static bool DigitalOnly(bool? digital, string? commanderLegality)
        => digital == true && commanderLegality == "not_legal";

    /// <summary>
    /// Front-face mana value: parsed from the front face's mana cost when the
    /// card has faces (Scryfall's top-level <c>cmc</c> is the whole-card value
    /// for split layouts), otherwise the top-level <c>cmc</c>.
    /// </summary>
    public static decimal ManaValue(SnapshotCard card)
        => card.Faces.Count > 0 && card.Faces[0].ManaCost is { } frontCost
            ? ManaValueOf(frontCost)
            : card.Cmc;

    /// <summary>Mana value of a "{2}{R}{G}"-style cost string.</summary>
    public static decimal ManaValueOf(string manaCost)
    {
        var total = 0m;
        foreach (var symbol in manaCost.TrimEnd('}').Split('{', StringSplitOptions.RemoveEmptyEntries))
        {
            var body = symbol.TrimEnd('}');
            // {2/W} costs its numeric half; {W/U} and {W/P} cost 1; X/Y/Z
            // cost 0; {HW} (half mana) costs one half.
            var head = body.Split('/')[0];
            if (decimal.TryParse(head, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric))
            {
                total += numeric;
            }
            else if (head.Length == 2 && head[0] == 'H')
            {
                total += 0.5m;
            }
            else if (head is not ("X" or "Y" or "Z"))
            {
                total += 1m;
            }
        }

        return total;
    }

    /// <summary>
    /// Commander eligibility: a legendary creature (judged on the front face
    /// for multi-faced cards), or card text containing "can be your
    /// commander". Never a meld result, which is not a card of its own.
    /// </summary>
    public static bool CanBeCommander(SnapshotCard card) => CanBeCommander(card, CombinedOracleText(card));

    private static bool CanBeCommander(SnapshotCard card, string text)
    {
        if (card.IsMeldResult)
        {
            return false;
        }

        var frontTypeLine = card.Faces.Count > 0 ? card.Faces[0].TypeLine : card.TypeLine;
        var (frontSupertypes, frontCardTypes, _) = ParseTypeLine(frontTypeLine);
        return (frontSupertypes.Contains("Legendary") && frontCardTypes.Contains("Creature"))
            || text.Contains("can be your commander");
    }

    /// <summary>
    /// Commander eligibility in the three Brawl formats (ADR 0012): the
    /// Commander rule, plus a legendary planeswalker, plus a legendary
    /// Vehicle or Spacecraft with power and toughness — each judged on the
    /// front face for multi-faced cards. Never a meld result.
    /// </summary>
    public static bool CanBeBrawlCommander(SnapshotCard card) => CanBeBrawlCommander(card, CombinedOracleText(card));

    private static bool CanBeBrawlCommander(SnapshotCard card, string text)
    {
        if (CanBeCommander(card, text))
        {
            return true;
        }

        if (card.IsMeldResult)
        {
            return false;
        }

        var front = card.Faces.Count > 0 ? card.Faces[0] : null;
        var (supertypes, cardTypes, subtypes) = ParseTypeLine(front?.TypeLine ?? card.TypeLine);
        if (!supertypes.Contains("Legendary"))
        {
            return false;
        }

        var hasPowerAndToughness = (front is null ? card.Power : front.Power) is not null
            && (front is null ? card.Toughness : front.Toughness) is not null;
        return cardTypes.Contains("Planeswalker")
            || ((subtypes.Contains("Vehicle") || subtypes.Contains("Spacecraft")) && hasPowerAndToughness);
    }

    /// <summary>
    /// Per-deck allowance: NULL (unlimited) for the Basic supertype or "any
    /// number of cards named" text; the printed count for "up to N cards
    /// named" text (Seven Dwarves' seven, Nazgûl's nine); 1 (singleton)
    /// otherwise.
    /// </summary>
    private static int? CopyLimit(SnapshotCard card, string[] supertypes, string text)
    {
        if (supertypes.Contains("Basic") || text.Contains("any number of cards named"))
        {
            return null;
        }

        const string upTo = "A deck can have up to ";
        var at = text.IndexOf(upTo, StringComparison.Ordinal);
        if (at >= 0)
        {
            var count = text[(at + upTo.Length)..].Split(' ')[0];
            if (NumberWords.TryGetValue(count, out var limit))
            {
                return limit;
            }
        }

        return 1;
    }

    private static readonly Dictionary<string, int> NumberWords = new()
    {
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
        ["ten"] = 10,
        ["eleven"] = 11,
        ["twelve"] = 12,
        ["thirteen"] = 13,
        ["fourteen"] = 14,
        ["fifteen"] = 15,
    };

    /// <summary>The card types that stay on the battlefield (CR 110.4a).</summary>
    public static readonly HashSet<string> PermanentTypes =
        ["Artifact", "Battle", "Creature", "Enchantment", "Land", "Planeswalker"];

    /// <summary>
    /// Splits a Scryfall type line ("Legendary Creature — Human Ranger",
    /// multi-faced "A // B") into distinct supertypes, card types, and
    /// subtypes across all faces.
    /// </summary>
    public static (string[] Supertypes, string[] CardTypes, string[] Subtypes) ParseTypeLine(string typeLine)
    {
        List<string> supertypes = [], cardTypes = [], subtypes = [];
        foreach (var face in typeLine.Split(" // "))
        {
            var sides = face.Split(" — ", 2);
            foreach (var word in sides[0].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                AddDistinct(KnownSupertypes.Contains(word) ? supertypes : cardTypes, word);
            }

            if (sides.Length > 1)
            {
                foreach (var subtype in SplitSubtypes(sides[1], cardTypes))
                {
                    AddDistinct(subtypes, subtype);
                }
            }
        }

        return ([.. supertypes], [.. cardTypes], [.. subtypes]);
    }

    private static IEnumerable<string> SplitSubtypes(string segment, List<string> cardTypes)
    {
        // Plane subtypes are a single multi-word name ("Plane — Serra's
        // Realm"); everything else is space-separated words, except the
        // known multi-word subtypes.
        if (cardTypes.Contains("Plane"))
        {
            yield return segment;
            yield break;
        }

        var words = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var pair = i + 1 < words.Length ? $"{words[i]} {words[i + 1]}" : null;
            if (pair is not null && MultiwordSubtypes.Contains(pair))
            {
                yield return pair;
                i++;
            }
            else
            {
                yield return words[i];
            }
        }
    }

    private static readonly HashSet<string> KnownSupertypes =
        ["Basic", "Legendary", "Snow", "World", "Ongoing", "Elite", "Host"];

    private static readonly HashSet<string> MultiwordSubtypes = ["Time Lord"];

    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }

    private static string CombinedOracleText(SnapshotCard card)
        => string.Join('\n', new[] { card.OracleText }
            .Concat(card.Faces.Select(face => face.OracleText))
            .Where(text => text is not null));
}
