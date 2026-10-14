using System.Text.RegularExpressions;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Ingest;

/// <summary>
/// Derives one <see cref="CardFeatureProfile"/> per card face from the
/// face's Oracle text and typed columns (spec §5.1). The parse first removes
/// reminder text and quoted granted-ability text, as the enters-tapped parse
/// of <see cref="SourceProfileDerivation"/> does. It reads each line as one
/// ability: activated ("cost: effect"), triggered ("When", "Whenever",
/// "At"), the rules text of an instant or sorcery, or other. It reads each
/// ability sentence by sentence against three closed template families:
/// <list type="bullet">
/// <item>Mana: <c>Add {…}</c>, <c>Add {X}[, {Y},] or {Z}</c>, and <c>Add one
/// mana of any color[ in your commander's color identity]</c> (one option
/// per color). A "{T}" activation fills tap mana. The "Exile this card from
/// your hand" activation fills hand mana. Fixed mana in an instant or
/// sorcery fills spell mana. Any other activation that adds mana only
/// develops. A land face taps for one option per produced color, as our
/// engine plays it, so its own "Add" sentences are not parsed.</item>
/// <item>Draws: <c>[you | each player | target player ]draw a card | N
/// cards | cards equal to …</c>, with an optional leading <c>gain N life
/// and </c> or trailing <c> and [you ]lose|gain N life</c> or <c> and create
/// a Treasure token</c>, in any ability, after its trigger condition. Only a
/// draw by the card's controller counts. "Each player" includes the
/// controller, and XMage's AI targets itself with a "target player" draw. A
/// clause that opens on its verb ("…, then draws seven cards") takes the
/// subject of the clause before it. A draw or search by target opponent,
/// each opponent, each other player or an opponent matches and adds
/// nothing: that player is never the controller. Any other subject ("that
/// player", "they", "its controller") may not be the controller, so it is
/// outside the family: unprofiled, not silently dropped.</item>
/// <item>Tutor: <c>Search your library for …[, reveal it], put it into your
/// hand | into your graveyard | onto the battlefield [tapped][, discard a
/// card at random], then shuffle</c> or <c>…, then shuffle and put that card
/// on top</c>, in a
/// spell, an activated ability or a Saga chapter whose sentence carries no
/// trigger condition. A chapter's search resolves chapter − 1 turns after
/// the Saga enters (<see cref="TutorVia.SagaChapter"/>); a chapter's mana
/// is triggered mana, so it skips the mana templates. A delayed trigger inside a spell or an activated ability
/// ("Whenever …, search …" or "…, add {…}") reads as a plain clause instead
/// of this template or the mana templates, so it is unprofiled rather than
/// misread as a cast or an activation (a delayed controller draw still
/// counts). An activation that exiles the card from the graveyard reads the
/// same way: <see cref="TutorVia"/> has no graveyard value, and its mana is
/// no battlefield mana. It adds no <c>Develops</c> of its own and no tutor
/// feature, but its controller draw counts. The face's produced colors
/// still make it develop. The first search in text order is the
/// tutor.</item>
/// </list>
/// A face develops when it has a produced color (a nonland face ignores the colors when all of its Treasures go to other players), any matched mana, or a
/// search onto the battlefield whose targets the fetch-template parser reads
/// as lands. A sentence that mentions draw, search or add and matches no
/// template makes the face unprofiled: the profile keeps what the templates
/// proved, and the caller reports the first such sentence. The parse never
/// guesses. Cycling, typecycling and transmute keep their effect only in
/// reminder text, so their keyword line reads as the activated ability its
/// reminder text states ("{2}, Discard this card: Draw a card"). Such a
/// line with no reminder text is unprofiled. A keyword activated ability
/// (CR 702: equip, fortify,
/// reconfigure, crew, cycling and typecycling, transmute, ninjutsu, level
/// up, unearth, scavenge, outlast, embalm, eternalize, saddle) sets
/// <c>HasActivatedAbility</c> from its keyword line alone, matched when the
/// keyword is directly followed by its cost, or for equip (CR 702.6e) by the
/// words of a qualified equip ("Equip Dinosaur", "Equip legendary
/// creature") then its cost; its reminder-text effect adds no other feature
/// and no report entry. The response feature comes from
/// <see cref="ResponseDerivation"/> over the same rules text, with its own
/// mention set and its own unprofiled flag; it changes no feature above.
/// </summary>
public static partial class CardFeatureDerivation
{
    private const string TapCost = "{T}";

    private const string HandExileCost = "Exile this card from your hand";

    private static readonly string[] AnyColor = ["{W}", "{U}", "{B}", "{R}", "{G}"];

    /// <summary>One face as the derivation reads it: its name, its card
    /// types, its Oracle text, and the Scryfall colors it makes.</summary>
    public sealed record Face(string Name, IReadOnlyList<string> CardTypes, string? OracleText, IReadOnlyList<string>? ProducedColors)
    {
        public bool IsLand => CardTypes.Contains("Land");

        public bool IsSpell => CardTypes.Contains("Instant") || CardTypes.Contains("Sorcery");
    }

    /// <summary>The face's profile. <paramref name="UnmatchedSentence"/> is
    /// the first sentence no template matched; null when the face is
    /// profiled. <paramref name="ResponseUnmatchedSentence"/> is the first
    /// sentence that mentions an answer and that no response family matched
    /// (<see cref="ResponseDerivation"/>); null when the face is
    /// response-profiled. The two flags are separate.</summary>
    public sealed record Result(CardFeatureProfile Profile, string? UnmatchedSentence, string? ResponseUnmatchedSentence = null)
    {
        public bool Unprofiled => UnmatchedSentence is not null;

        public bool ResponseUnprofiled => ResponseUnmatchedSentence is not null;
    }

    /// <summary>One stored face row, as the phase reads it.</summary>
    public sealed record FaceRow(string Name, string TypeLine, string? OracleText, string[]? ProducedColors);

    /// <summary>
    /// The faces the derivation reads: the card's face rows in face order,
    /// or the card itself as face 0 when it has none. A card with no face
    /// rows makes its <c>produced_mana</c> colors. A face row makes its own
    /// colors (the source-profile phase sets them on a modal double-faced
    /// card's land faces), else the card's colors when it is a land face (a
    /// transform land), else none: a nonland face shows only what its text
    /// adds.
    /// </summary>
    public static IReadOnlyList<Face> FacesOf(
        string name, string typeLine, string? oracleText, string[]? producedMana, IReadOnlyList<FaceRow> faces)
    {
        if (faces.Count == 0)
        {
            return [new Face(name, CardDerivations.ParseTypeLine(typeLine).CardTypes, oracleText, producedMana)];
        }

        return
        [
            .. faces.Select(face =>
            {
                var types = CardDerivations.ParseTypeLine(face.TypeLine).CardTypes;
                return new Face(face.Name, types, face.OracleText, face.ProducedColors ?? (types.Contains("Land") ? producedMana : null));
            }),
        ];
    }

    public static Result Derive(Face face)
    {
        var rulesText = RulesText(face);
        var profile = new ProfileBuilder(face, rulesText);
        foreach (var line in rulesText.Split('\n'))
        {
            if (Ability.Of(line.Trim(), face.IsSpell) is not { } ability)
            {
                continue;
            }

            profile.HasActivatedAbility |= ability.Kind == AbilityKind.Activated || KeywordActivatedLine().IsMatch(ability.Effect);
            if (ReminderKeywordLine().IsMatch(ability.Effect))
            {
                profile.Unmatched ??= ability.Effect;
            }

            var sentences = SourceProfileDerivation.SentenceBreak().Split(ability.Effect)
                .Select(sentence => sentence.Trim().TrimEnd('.').TrimEnd())
                .Where(sentence => sentence.Length > 0);
            foreach (var sentence in sentences)
            {
                if (!ReadSentence(sentence, ability, profile))
                {
                    profile.Unmatched ??= sentence;
                }
            }
        }

        var built = profile.Build();
        var response = ResponseDerivation.Derive(face, rulesText);
        return built with
        {
            Profile = built.Profile with { Response = response.Response },
            ResponseUnmatchedSentence = response.UnmatchedSentence,
        };
    }

    /// <summary>The face's own rules text: reminder text and quoted
    /// granted-ability text removed. A cycling, typecycling or transmute
    /// line becomes the activated ability its reminder text states first. A
    /// face whose name holds a template word ("Search for Azcanta") calls
    /// itself by name, so the name becomes "this card" first.</summary>
    private static string RulesText(Face face)
    {
        var abilities = KeywordReminder().Replace(face.OracleText ?? "", "${ability}");
        var text = SourceProfileDerivation.NotRulesText().Replace(abilities, "");
        return Mentions(face.Name) ? SourceProfileDerivation.SelfName(face.Name).Replace(text, "this card") : text;
    }

    /// <summary>Reads one sentence into the profile. False when the sentence
    /// mentions draw, search or add and no template matches it. A trigger
    /// condition ("Whenever you draw a card, ") and a leading "Then" are not
    /// the effect, so they go first. A delayed trigger inside a spell or an
    /// activated ability ("Whenever …, add {R}") is a plain clause, not a
    /// cast or an activation, so it skips the tutor and mana templates.</summary>
    private static bool ReadSentence(string sentence, Ability ability, ProfileBuilder profile)
    {
        var triggered = TriggerCondition().IsMatch(sentence);
        var effect = LeadingThen().Replace(TriggerCondition().Replace(sentence, ""), "");
        if (!Mentions(effect))
        {
            return true;
        }

        if (profile.Land && IsLandMana(effect))
        {
            return true;
        }

        if (ability.Kind is AbilityKind.Spell or AbilityKind.Activated or AbilityKind.Chapter && !triggered && !FromGraveyard(ability))
        {
            if (SearchSentence().Match(effect) is { Success: true } search)
            {
                profile.AddTutor(search, effect, ability);
                return true;
            }

            if (ability.Kind != AbilityKind.Chapter && ManaOptions(effect) is { } mana)
            {
                return profile.AddMana(mana, ability);
            }
        }

        return ReadClauses(effect, profile);
    }

    /// <summary>An activation that exiles the card itself from the
    /// graveyard. Its search and its mana read as plain clauses (see the
    /// class summary). A cost that exiles another card from the graveyard
    /// ("Exile a card from your graveyard") is paid on the battlefield.</summary>
    private static bool FromGraveyard(Ability ability)
        => ability.Cost?.Contains("this card from your graveyard", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Reads the ", then"-joined clauses of a sentence. Each clause
    /// that mentions a template word must be a draw by the controller or
    /// another player's draw or search. False for any other clause. A clause
    /// that opens on its verb ("then draws seven cards") takes the subject
    /// of the clause before it.</summary>
    private static bool ReadClauses(string effect, ProfileBuilder profile)
    {
        string? subject = null;
        foreach (var part in ClauseBreak().Split(effect))
        {
            var clause = subject is not null && BareVerb().IsMatch(part) ? $"{subject} {part}" : part;
            subject = Subject().Match(clause) is { Success: true } named ? named.Value : null;
            if (!Mentions(clause) || OtherPlayerClause().IsMatch(clause))
            {
                continue;
            }

            if (!DrawClause().IsMatch(clause))
            {
                return false;
            }

            profile.Draws = true;
        }

        return true;
    }

    /// <summary>True when the rules text creates mana tokens and each one goes
    /// to another player. Scryfall lists the Treasure colors for any
    /// recipient. Those colors do not show mana for the controller. A token
    /// for the controller ("Create", "you create", "each player creates") or
    /// a token with an unknown recipient keeps the colors. So does any other
    /// sentence that adds mana: a template may not match it, and the colors
    /// are then the only sign that the face develops.</summary>
    private static bool TokensOnlyForOthers(string rulesText)
    {
        if (AddsMana().IsMatch(rulesText))
        {
            return false;
        }

        var found = false;
        foreach (var line in rulesText.Split('\n'))
        {
            if (GiftToken().IsMatch(line.Trim()))
            {
                found = true;
                continue;
            }

            var opponentNamed = false;
            foreach (var sentence in SourceProfileDerivation.SentenceBreak().Split(line))
            {
                if (TokenCreation().IsMatch(sentence))
                {
                    if (!CreatedByOther(sentence, opponentNamed))
                    {
                        return false;
                    }

                    found = true;
                }

                opponentNamed |= OpponentWord().IsMatch(sentence);
            }
        }

        return found;
    }

    /// <summary>True when another player creates the token of this sentence.
    /// A sentence with a second creation ("…creates a Treasure token and you
    /// create a Treasure token") may name the controller too, so it is not
    /// another player's.</summary>
    private static bool CreatedByOther(string sentence, bool opponentNamed)
        => CreateWord().Matches(sentence).Count == 1
            && (OtherPlayerCreates().IsMatch(sentence) || (opponentNamed && TheyCreate().IsMatch(sentence)));
    private static bool Mentions(string text)
        => AddWord().IsMatch(text) || DrawWord().IsMatch(text) || SearchWord().IsMatch(text);

    /// <summary>A land's own "Add" sentence: its produced colors already
    /// cover it (see the class summary).</summary>
    private static bool IsLandMana(string sentence)
        => AddWord().IsMatch(sentence) && !DrawWord().IsMatch(sentence) && !SearchWord().IsMatch(sentence);

    /// <summary>The options an "Add" sentence offers, or null outside the
    /// family. Fixed mana is one option; a list or "any color" is one option
    /// per symbol.</summary>
    private static ManaAdd? ManaOptions(string sentence)
    {
        if (FixedMana().Match(sentence) is { Success: true } fixedMana)
        {
            return new ManaAdd([fixedMana.Groups["mana"].Value], Fixed: true);
        }

        if (ManaChoice().Match(sentence) is { Success: true } choice)
        {
            return new ManaAdd([.. choice.Groups["symbol"].Captures.Select(capture => capture.Value)], Fixed: false);
        }

        return AnyColorMana().IsMatch(sentence) ? new ManaAdd(AnyColor, Fixed: false) : null;
    }

    /// <summary>An activation that discards the card or works from hand
    /// starts from hand; any other starts on the battlefield.</summary>
    private static TutorVia ViaOf(string cost)
        => cost.Contains("from your hand", StringComparison.Ordinal) || cost.Contains("Discard this card", StringComparison.Ordinal)
            ? TutorVia.ActivateFromHand
            : TutorVia.ActivateOnBattlefield;

    /// <summary>The mana symbols of an activation cost ("{2}"); null when
    /// the cost has none ("{T}, Sacrifice this land").</summary>
    private static string? ManaCostOf(string cost)
    {
        var mana = string.Concat(ManaSymbols.Parse(cost)
            .Where(symbol => symbol.Kind != ManaSymbolKind.Other)
            .Select(symbol => $"{{{symbol.Text}}}"));
        return mana.Length > 0 ? mana : null;
    }

    private sealed record ManaAdd(IReadOnlyList<string> Options, bool Fixed);

    internal enum AbilityKind
    {
        /// <summary>A line of an instant's or sorcery's rules text.</summary>
        Spell,

        /// <summary>"Cost: effect" (CR 602.1).</summary>
        Activated,

        /// <summary>"When", "Whenever" or "At" (CR 603.1).</summary>
        Triggered,

        /// <summary>A Saga chapter ability, "III — effect" (CR 714.2b).</summary>
        Chapter,

        /// <summary>A static ability, a keyword line, or a mode.</summary>
        Other,
    }

    /// <summary>One line of rules text as an ability. <paramref name="Cost"/>
    /// is set for an activated ability only. <paramref name="Chapter"/> is
    /// the first chapter of a chapter ability, 0 otherwise.</summary>
    internal sealed record Ability(AbilityKind Kind, string? Cost, string Effect, int Chapter = 0)
    {
        /// <summary>Reads one line; null for an empty line. A mode ("• …") is
        /// Other. A chapter line ("III — ", "II, III — ") is a chapter
        /// ability at its first chapter. An ability word ("Channel — ") is
        /// dropped first.</summary>
        public static Ability? Of(string line, bool spellFace)
        {
            if (line.Length == 0)
            {
                return null;
            }

            if (line.StartsWith("• ", StringComparison.Ordinal))
            {
                return new Ability(AbilityKind.Other, null, line[2..]);
            }

            if (ChapterLine().Match(line) is { Success: true } chapter)
            {
                return new Ability(AbilityKind.Chapter, null, chapter.Groups["effect"].Value, Roman(chapter.Groups["first"].Value));
            }

            var body = AbilityWord().Replace(line, "");
            if (ActivatedLine().Match(body) is { Success: true } activated)
            {
                return new Ability(AbilityKind.Activated, activated.Groups["cost"].Value, activated.Groups["effect"].Value);
            }

            if (TriggerWord().IsMatch(body))
            {
                return new Ability(AbilityKind.Triggered, null, body);
            }

            return new Ability(spellFace ? AbilityKind.Spell : AbilityKind.Other, null, body);
        }

        /// <summary>A chapter numeral (CR 714.2a): I to VI.</summary>
        private static int Roman(string numeral) => Array.IndexOf<string>(["I", "II", "III", "IV", "V", "VI"], numeral) + 1;
    }

    /// <summary>The profile one face builds, sentence by sentence.</summary>
    private sealed class ProfileBuilder
    {
        private readonly bool _produces;

        public ProfileBuilder(Face face, string rulesText)
        {
            Land = face.IsLand;
            _produces = face.ProducedColors is { Count: > 0 } && (face.IsLand || !TokensOnlyForOthers(rulesText));
            if (Land)
            {
                TapMana.AddRange((face.ProducedColors ?? []).Select(color => $"{{{color}}}"));
            }
        }

        public bool Land { get; }

        public bool Develops { get; set; }

        public bool Draws { get; set; }

        public List<string> TapMana { get; } = [];

        public List<string> HandMana { get; } = [];

        public string? SpellMana { get; set; }

        public TutorFeature? Tutor { get; set; }

        public bool HasActivatedAbility { get; set; }

        public string? Unmatched { get; set; }

        /// <summary>Puts matched mana where its ability sends it. False for a
        /// choice of mana in a spell: spell mana is one fixed string.</summary>
        public bool AddMana(ManaAdd mana, Ability ability)
        {
            if (ability.Kind == AbilityKind.Spell)
            {
                if (!mana.Fixed)
                {
                    return false;
                }

                SpellMana += mana.Options[0];
            }
            else if (ability.Cost == TapCost)
            {
                TapMana.AddRange(mana.Options);
            }
            else if (ability.Cost == HandExileCost)
            {
                HandMana.AddRange(mana.Options);
            }

            Develops = true;
            return true;
        }

        /// <summary>Records a matched search. The first search is the tutor.
        /// A search onto the battlefield develops when the fetch-template
        /// parser reads its targets as lands.</summary>
        public void AddTutor(Match search, string effect, Ability ability)
        {
            var destination = search.Groups["to"].Value.ToLowerInvariant() switch
            {
                "into your hand" => TutorDestination.Hand,
                "into your graveyard" => TutorDestination.Graveyard,
                "on top" => TutorDestination.LibraryTop,
                _ => TutorDestination.Battlefield,
            };
            if (destination == TutorDestination.Battlefield && SourceProfileDerivation.Derive(null, effect).Fetch is not null)
            {
                Develops = true;
            }

            var tapped = search.Groups["tapped"].Success;
            Tutor ??= ability.Kind == AbilityKind.Chapter
                ? new TutorFeature(destination, tapped, TutorVia.SagaChapter, null, ability.Chapter - 1)
                : ability.Cost is { } cost
                    ? new TutorFeature(destination, tapped, ViaOf(cost), ManaCostOf(cost))
                    : new TutorFeature(destination, tapped, TutorVia.Cast, null);
        }

        public Result Build() => new(
            new CardFeatureProfile(
                Develops || _produces,
                Draws,
                TapMana.Distinct(StringComparer.Ordinal).ToList(),
                HandMana.Distinct(StringComparer.Ordinal).ToList(),
                SpellMana,
                Tutor,
                HasActivatedAbility),
            Unmatched);
    }

    /// <summary>Matches a sentence that creates a mana token. These are the
    /// tokens that <see cref="SourceProfileDerivation"/> counts as mana.</summary>
    [GeneratedRegex(@"\bcreates?\b[^.\n]*\b(?:Treasure|Gold|Powerstone)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TokenCreation();

    /// <summary>Matches a Gift keyword line for a mana token. An opponent gets
    /// the token (CR 702.174).</summary>
    [GeneratedRegex(@"^Gift an? (?:Treasure|Gold|Powerstone)\b")]
    private static partial Regex GiftToken();

    /// <summary>Matches a creation by another player: an opponent, or the
    /// controller of a countered spell or an exiled permanent.</summary>
    [GeneratedRegex(@"(?:^|, |\. )(?:its controller|target opponent|an opponent|each opponent) creates?\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherPlayerCreates();

    [GeneratedRegex(@"(?:^|, )they create\b", RegexOptions.IgnoreCase)]
    private static partial Regex TheyCreate();

    [GeneratedRegex(@"\bcreates?\b", RegexOptions.IgnoreCase)]
    private static partial Regex CreateWord();

    /// <summary>Matches a sentence that adds mana, with a mana symbol or the
    /// word "mana" after the verb.</summary>
    [GeneratedRegex(@"\badds?\b[^.\n]*?(?:\{[WUBRGC]\}|\bmana\b(?! value))", RegexOptions.IgnoreCase)]
    private static partial Regex AddsMana();

    [GeneratedRegex(@"\bopponents?\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpponentWord();

    /// <summary>An ability word before an em dash.</summary>
    [GeneratedRegex(@"^[A-Z][^—:.{]*? — ")]
    private static partial Regex AbilityWord();

    /// <summary>A chapter ability: its chapter numerals, then an em dash
    /// (CR 714.2b).</summary>
    [GeneratedRegex(@"^(?<first>I|II|III|IV|V|VI)(?:, (?:I|II|III|IV|V|VI))* — (?<effect>.+)$")]
    private static partial Regex ChapterLine();

    /// <summary>"Cost: effect". The cost holds no sentence and opens no
    /// trigger or replacement.</summary>
    [GeneratedRegex(@"^(?<cost>(?!(?:When|Whenever|At|As|If)\b)[^:.]+?): (?<effect>.+)$")]
    private static partial Regex ActivatedLine();

    [GeneratedRegex(@"^(?:When|Whenever|At)\b")]
    private static partial Regex TriggerWord();

    [GeneratedRegex(@"\badds?\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddWord();

    /// <summary>"draw step" names a step, not a draw.</summary>
    [GeneratedRegex(@"\bdraws?\b(?! step)", RegexOptions.IgnoreCase)]
    private static partial Regex DrawWord();

    [GeneratedRegex(@"\bsearch(?:es)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SearchWord();

    [GeneratedRegex(@"^add (?<mana>(?:\{[WUBRGC]\})+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FixedMana();

    [GeneratedRegex(@"^add (?<symbol>\{[WUBRGC]\})(?:, (?<symbol>\{[WUBRGC]\}))*,? or (?<symbol>\{[WUBRGC]\})$", RegexOptions.IgnoreCase)]
    private static partial Regex ManaChoice();

    [GeneratedRegex(@"^add one mana of any color(?: in your commander's color identity)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AnyColorMana();

    /// <summary>A trigger condition and an intervening "if" clause
    /// (CR 603.4).</summary>
    [GeneratedRegex(@"^(?:When|Whenever|At)\b[^,]*, (?:if [^,]*, )?")]
    private static partial Regex TriggerCondition();

    [GeneratedRegex(@"^Then ")]
    private static partial Regex LeadingThen();

    [GeneratedRegex(@", then ")]
    private static partial Regex ClauseBreak();

    /// <summary>A draw by the card's controller: "[you ]draw", or "each
    /// player draws" and "target player draws" (XMage's AI targets itself
    /// for a draw). A bare "draws" has a subject this family does not know,
    /// so it does not match. The count is a number word, X, or "cards equal
    /// to …". An optional leading "gain N life and" or trailing "and [you
    /// ]lose|gain N life" or "and create a Treasure token" adds nothing: a
    /// Treasure is no template mana.</summary>
    [GeneratedRegex(
        @"^(?:(?:you )?(?:gain \d+ life and )?draw|(?:each player|target player) draws) "
        + @"(?:a card|(?:two|three|four|five|six|seven|eight|nine|ten|X) cards|cards equal to [^,]+?)"
        + @"(?: and (?:you )?(?:lose|gain)s? \d+ life| and create (?:a|one|two|three) Treasure tokens?)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex DrawClause();

    /// <summary>The player a clause opens on, for a later clause that opens
    /// on its verb. A qualified subject ("Each player who …") is no
    /// subject the family knows.</summary>
    [GeneratedRegex(
        @"^(?:you|each player|target player|target opponent|each other player|each opponent|an opponent|that player|they|its controller)\b(?! who\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Subject();

    /// <summary>A clause that opens on a draw or search verb, with no
    /// subject of its own.</summary>
    [GeneratedRegex(@"^(?:draws?|search(?:es)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BareVerb();

    /// <summary>A draw or search by a player who is never the controller.
    /// A subject that may not be the controller ("that player", "they",
    /// "its controller") is excluded. No draw clause matches it, so the face
    /// stays unprofiled.</summary>
    [GeneratedRegex(
        @"^(?:target opponent|each other player|each opponent|an opponent) (?:may )?(?:draws?|search(?:es)?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex OtherPlayerClause();

    /// <summary>A search of your own library with one destination and a
    /// shuffle. The target stops before the first ", put", ", reveal" or
    /// ", then", so a clause the family does not know fails the match. A
    /// random discard after the put (Gamble) is part of the template: the
    /// pilot reads no discard.</summary>
    [GeneratedRegex(
        @"^search your library for (?<target>(?:(?!, (?:put|reveal|then)\b).)+)"
        + @"(?:, reveal (?:it|that card|them|those cards))?"
        + @"(?:, put (?:it|that card|them|those cards) (?<to>into your hand|into your graveyard|onto the battlefield(?<tapped> tapped)?)(?:, discard a card at random)?, then shuffle"
        + @"|, then shuffle and put (?:it|that card|the card) (?<to>on top)(?: of your library)?)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SearchSentence();

    /// <summary>A keyword activated ability (CR 702): the keyword directly
    /// followed by its cost (a space then "{", a space then a digit, or an
    /// em dash), or a qualified equip (CR 702.6e: "Equip" then at most two
    /// words, "Equip legendary creature") followed the same way. "Cycling
    /// abilities you activate cost {1} less" and "Equip abilities you
    /// activate cost {1} less" name the keyword rather than paying it: the
    /// qualifier excludes "abilities" and stops before the cost, so neither
    /// matches.</summary>
    [GeneratedRegex(
        @"^(?:Equip(?: (?!abilities\b)[A-Za-z]+){0,2}|Fortify|Reconfigure|Crew|Basic landcycling|[A-Za-z]*cycling|Transmute|Ninjutsu|Level up|Unearth|Scavenge|Outlast|Embalm|Eternalize|Saddle)(?: \{| \d|—)",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeywordActivatedLine();

    /// <summary>A cycling, typecycling or transmute line with its reminder
    /// text: the keyword, its cost, then "(cost, Discard this card:
    /// effect)". The ability group is the reminder text's activated
    /// ability.</summary>
    [GeneratedRegex(
        @"^(?:Basic landcycling|[A-Za-z]*cycling|Transmute)\b[^(\n]*\((?<ability>[^()\n]+: [^()\n]+)\)[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex KeywordReminder();

    /// <summary>A cycling, typecycling or transmute line that is left after
    /// <see cref="KeywordReminder"/>: the keyword and its cost with no
    /// reminder text to read its effect from.</summary>
    [GeneratedRegex(@"^(?:Basic landcycling|[A-Za-z]*cycling|Transmute)(?: \{| \d|—)", RegexOptions.IgnoreCase)]
    private static partial Regex ReminderKeywordLine();
}
