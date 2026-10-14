using System.Collections.Concurrent;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Simulation.Xmage;

/// <summary>
/// The XMage adapter's pilot view builder (glossary: Engine adapter). It
/// maps a wire view onto <see cref="PilotView"/>: each decklist name onto
/// the deck's own <see cref="DeckCard"/>, a back face onto
/// <see cref="DeckCard.AsBackFace"/>, each action onto an
/// <see cref="EngineAction"/> with the features of its card feature
/// profile. It reports a feature only for an effect XMage runs: a blank
/// card (a card XMage lacks) and a name outside the deck (a token) have
/// none. It holds nothing of one run, so the runs of a batch share it.
/// </summary>
public sealed class XmageViews
{
    /// <summary>The wire id of a pick's stop option.</summary>
    public const string StopId = "none";

    private static readonly string[] Colors = ["W", "U", "B", "R", "G"];

    private readonly Dictionary<string, DeckCard> _byName = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, DeckCard> _outside = new(StringComparer.Ordinal);

    private readonly HashSet<string> _unreadSeen = new(StringComparer.Ordinal);

    private readonly Lock _unreadLock = new();

    private readonly DeckFeatureProfiles _profiles;

    private readonly HashSet<string> _blanks;

    /// <param name="deck">The decklist: each card's full name maps to it, and a multi-face card's front face name too.</param>
    /// <param name="profiles">The deck's card feature profiles.</param>
    /// <param name="blanks">The cards XMage lacks: they have no features.</param>
    public XmageViews(IReadOnlyList<DeckCard> deck, DeckFeatureProfiles profiles, IEnumerable<string> blanks)
    {
        Deck = deck;
        foreach (var card in deck)
        {
            _byName.TryAdd(card.Name, card);
        }

        foreach (var card in deck)
        {
            if (card.FrontFaceName is { } front)
            {
                _byName.TryAdd(front, card);
            }
        }

        _profiles = profiles;
        _blanks = new HashSet<string>(blanks, StringComparer.Ordinal);
    }

    /// <summary>Every mana string the views left out because the pilot
    /// cannot read it (not W, U, B, R, G, C or {Any}), each once, in ordinal
    /// order. Ordinal order (not first-encounter order) keeps this
    /// deterministic across a batch's parallel runs, which all share one
    /// <see cref="XmageViews"/>. The live tests assert it stays empty on
    /// real decks.</summary>
    public IReadOnlyCollection<string> UnreadMana
    {
        get
        {
            lock (_unreadLock)
            {
                return [.. _unreadSeen.Order(StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>Every name the views met outside the deck: tokens, or a
    /// card XMage names another way.</summary>
    public IReadOnlyCollection<string> OutsideNames => (IReadOnlyCollection<string>)_outside.Keys;

    /// <summary>The decklist the views map names onto, commanders included.</summary>
    public IReadOnlyList<DeckCard> Deck { get; }

    /// <summary>
    /// The cards a counterspell's rule treats as reachable (ADR 0005): the
    /// hand, the command zone and the library. The wire view never shows
    /// the library, so the library is the deck less every card the view
    /// shows and less the spell on the stack. The disruption schedule reads
    /// what the library holds, never its order; the pilot never sees it.
    /// A token namesake, a clone of a deck card, or a card of ours in a zone
    /// the view does not show can shift this estimate away from the true
    /// library (roadmap item 21).
    /// </summary>
    public IEnumerable<DeckCard> Reachable(PilotView view, DeckCard spell)
    {
        var left = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var card in Deck.Where(c => !c.IsCommander))
        {
            left[card.Name] = left.GetValueOrDefault(card.Name) + card.Quantity;
        }

        foreach (var card in view.Hand.Concat(view.Graveyard).Concat(view.Exile).Concat(view.BattlefieldCards).Append(spell))
        {
            var name = card.Whole().Name;
            if (left.TryGetValue(name, out var count))
            {
                left[name] = count - 1;
            }
        }

        return view.Hand.Concat(view.CommandZone).Concat(Deck.Where(c => !c.IsCommander && left.GetValueOrDefault(c.Name) > 0));
    }

    public PilotView View(WireView view) => new(
        view.Turn,
        Step(view.Step),
        view.Life,
        view.LandDropUsed,
        new ManaBoard(
            Pool(view.Pool),
            view.Life,
            [.. view.ManaSources.Select(s => new BoardManaSource(s.Id, ManaOptions(s.Options))).Where(s => s.Options.Count > 0)],
            [.. view.HandMana.Select(h => new HandManaOption(Card(h.Card), ManaOptions(h.Options))).Where(h => h.Options.Count > 0)]),
        [.. view.Hand.Select(Card)],
        [.. view.Battlefield.Select(p => new ViewPermanent(p.Id, Face(p.Card, p.Face), p.Tapped))],
        [.. view.Graveyard.Select(Card)],
        [.. view.Exile.Select(Card)],
        [.. view.CommandZone.Select(Card)],
        [.. view.Resolved.Select(r => new ResolvedCast(Face(r.Card, r.Face), r.Turn))],
        view.Finds.ToDictionary(f => f.Key, f => (IReadOnlyList<DeckCard>)[.. f.Value.Select(Card)], StringComparer.Ordinal),
        [.. view.Actions.Select(Action)],
        Features);

    /// <summary>
    /// A wire pick as the pilot sees it. The stop option (wire id
    /// <see cref="StopId"/>) becomes <see cref="PickOption.FindNothingId"/>
    /// for a search and <see cref="PickOption.KeepRestId"/> for a scry or a
    /// look (roadmap, pilot view). The wire's seen stays the same for every
    /// pick of one scry. A scry offers every card it has not put on the
    /// bottom, top first, so a scry's seen is its card options.
    /// </summary>
    public PickChoice Pick(WirePick pick)
    {
        var purpose = Purpose(pick.Purpose);
        List<PickOption> options = [.. pick.Options.Select(o => o.Id == StopId
            ? new PickOption(purpose == PickPurpose.Tutor ? PickOption.FindNothingId : PickOption.KeepRestId, o.Label, null)
            : new PickOption(o.Id, o.Label, o.Card is null ? null : Card(o.Card)))];
        var seen = purpose == PickPurpose.Scry && pick.Seen is not null
            ? options.Select(o => o.Card).OfType<DeckCard>().ToList()
            : pick.Seen?.Select(Card).ToList();
        return new PickChoice(purpose, options, seen);
    }

    /// <summary>The whole card of a decklist name. A name outside the deck
    /// becomes one featureless permanent card per name.</summary>
    public DeckCard Card(string name)
        => _byName.TryGetValue(name, out var card)
            ? card
            : _outside.GetOrAdd(name, n => new DeckCard { OracleId = Guid.Empty, Name = n, Quantity = 1, IsPermanent = true });

    /// <summary>The face the wire names: the back face of a modal
    /// double-faced card, else the whole card.</summary>
    public DeckCard Face(string name, string face)
        => face == "back" && Card(name).AsBackFace() is { } back ? back : Card(name);

    /// <summary>The card feature profile of a face. None for a blank card
    /// and for a card outside the deck.</summary>
    public CardFeatureProfile Features(DeckCard face)
    {
        var whole = face.Whole().Name;
        return _blanks.Contains(whole) || !_byName.ContainsKey(whole) ? CardFeatureProfile.None : _profiles.Of(face);
    }

    /// <summary>
    /// The mana strings the pilot can read: every symbol W, U, B, R, G or C.
    /// XMage writes a mana of any color as {Any}; such an option becomes
    /// one option per color, each {Any} that color. Any other string goes
    /// to <see cref="UnreadMana"/> and out of the view.
    /// </summary>
    public IReadOnlyList<string> ManaOptions(IEnumerable<string> options)
    {
        var read = new List<string>();
        foreach (var option in options)
        {
            IEnumerable<string> expanded = option.Contains("{Any}", StringComparison.Ordinal)
                ? Colors.Select(c => option.Replace("{Any}", "{" + c + "}", StringComparison.Ordinal))
                : [option];
            foreach (var mana in expanded)
            {
                if (!IsPoolMana(mana))
                {
                    AddUnread(option);
                }
                else if (!read.Contains(mana))
                {
                    read.Add(mana);
                }
            }
        }

        return read;
    }

    /// <summary>The wire id of the pilot's pick: a stop option goes back as <see cref="StopId"/>.</summary>
    public static string WireOptionId(string id) => id is PickOption.FindNothingId or PickOption.KeepRestId ? StopId : id;

    /// <summary>The wire name of a card in a reply: its full decklist name.</summary>
    public static string WireName(DeckCard card) => card.Whole().Name;

    public static TurnStep Step(string step) => step switch
    {
        "mulligan" => TurnStep.Mulligan,
        "untap" => TurnStep.Untap,
        "upkeep" => TurnStep.Upkeep,
        "draw" => TurnStep.Draw,
        "main" => TurnStep.Main,
        "end" => TurnStep.End,
        "cleanup" => TurnStep.Cleanup,
        _ => throw new InvalidDataException($"Step '{step}' is not in wire protocol {XmageWire.Protocol}."),
    };

    public static PickPurpose Purpose(string purpose) => purpose switch
    {
        "tutor" => PickPurpose.Tutor,
        "scry" => PickPurpose.Scry,
        "order_top" => PickPurpose.OrderTop,
        "shuffle" => PickPurpose.Shuffle,
        "put_back" => PickPurpose.PutBack,
        "name" => PickPurpose.Name,
        "trigger_order" => PickPurpose.TriggerOrder,
        "other" => PickPurpose.Other,
        _ => throw new InvalidDataException($"Pick purpose '{purpose}' is not in wire protocol {XmageWire.Protocol}."),
    };

    /// <summary>
    /// What an activation does, as far as the card's profile tells: the
    /// wire names the card, not the ability. Only a search that starts on
    /// the battlefield (a fetch land) is the ability's own, and it develops
    /// when the card develops and the search puts the card onto the
    /// battlefield. A draw on the card counts for the activation. The shim
    /// lists no mana ability, so an activation adds no mana.
    /// </summary>
    public static CardFeatureProfile ActivationFeatures(CardFeatureProfile card)
    {
        var tutor = card.Tutor is { Via: TutorVia.ActivateOnBattlefield } search ? search : null;
        return new CardFeatureProfile(
            Develops: card.Develops && tutor is { Destination: TutorDestination.Battlefield },
            Draws: card.Draws,
            TapMana: [],
            HandMana: [],
            SpellMana: null,
            Tutor: tutor,
            HasActivatedAbility: true);
    }

    private EngineAction Action(WireAction action)
    {
        var kind = ActionKind(action.Kind);
        var isActivation = kind == EngineActionKind.Activate;
        var face = Face(action.Card, action.Face);
        var cost = ManaCost.Parse(action.Cost);
        var profile = isActivation ? ActivationFeatures(Features(face)) : Features(face);
        IReadOnlyList<string> manaAdded = isActivation ? [] : ManaAdded(face, profile);
        return new EngineAction(action.Id, kind, face, cost, isActivation ? action.PermanentId : null, profile, manaAdded, isActivation && action.SpendsSource);
    }

    private static EngineActionKind ActionKind(string kind) => kind switch
    {
        "land" => EngineActionKind.PlayLand,
        "cast" => EngineActionKind.Cast,
        "cast_commander" => EngineActionKind.CastCommander,
        "activate" => EngineActionKind.Activate,
        _ => throw new InvalidDataException($"Action kind '{kind}' is not in wire protocol {XmageWire.Protocol}."),
    };

    /// <summary>The mana a land drop or a cast adds once it resolves: one
    /// tap of a permanent, or a spell's own mana.</summary>
    private static IReadOnlyList<string> ManaAdded(DeckCard face, CardFeatureProfile profile)
        => face.IsPermanent ? profile.TapMana : profile.SpellMana is { } mana ? [mana] : [];

    private ManaPool Pool(string pool)
    {
        if (pool.Length == 0 || IsPoolMana(pool))
        {
            return ManaPool.Parse(pool);
        }

        AddUnread(pool);
        return new ManaPool();
    }

    /// <summary>Records a mana string the pilot cannot read, once. Guarded
    /// with a lock: runs of a batch share one <see cref="XmageViews"/> and
    /// call this from many threads.</summary>
    private void AddUnread(string mana)
    {
        lock (_unreadLock)
        {
            _unreadSeen.Add(mana);
        }
    }

    /// <summary>Symbols only, each one color or C: "{C}{C}" reads, "{2}"
    /// and XMage's "20{C}" for twenty do not.</summary>
    private static bool IsPoolMana(string mana)
        => ManaSymbols.IsCost(mana) && ManaSymbols.Parse(mana).All(s => s.Kind == ManaSymbolKind.Color && s.Colors.Count == 1);
}
