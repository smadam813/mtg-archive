using System.Text.RegularExpressions;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// Derives a card's <see cref="SourceProfile"/> facets: produced colors are
/// verbatim non-empty <c>produced_mana</c> (no card-type filter — lands,
/// rocks, and dorks alike), and fetchable land types parse from the closed
/// "search your library for a … land" template family only. The family:
/// one or more <c>and</c>-joined targets of the shape
/// <c>a|an|up to N [basic] (land | &lt;type list&gt;) card(s)</c>, with the
/// closed type vocabulary below. A card with several search clauses
/// (landcycling plus an activated fetch, a face per side) unions them:
/// the profile is what the card can fetch, not any one ability. Any
/// search-land text outside the family — snow
/// constraints, "and/or", "up to that many", mixed basic and nonbasic
/// targets — is unmatched: no fetch facet, and the caller reports it. The
/// same hard-edge discipline as the card feature derivation: never guessed, never
/// silently dropped. A land whose every search clause is the closed
/// tap-and-sacrifice activation also records the found land's state
/// (<see cref="Fetch.Activation"/>) for tier 1 and the pilot.
///
/// A land's enters-tapped state parses from its land faces
/// alone, sentence by sentence. The parser first removes reminder text and
/// quoted granted-ability text: neither is the land's own rules text. It
/// then rewrites the face's self-references ("this land", the face name,
/// the name without the Alchemy "A-" prefix or a ", title" suffix) to
/// "this land". The family, one template per sentence:
/// <list type="bullet">
/// <item><c>this land enters [the battlefield] tapped[ with …]</c> is tapped.</item>
/// <item><c>this land enters tapped unless|if …</c> is conditional.</item>
/// <item><c>If|During|Unless …, this land enters tapped</c> is conditional.</item>
/// <item><c>If you don't, this land|it enters tapped</c> reads the sentence
/// before it. After <c>you may pay N|X life</c> it is untapped; after any
/// other <c>you may …</c> choice it is conditional; after anything else it
/// is unmatched.</item>
/// <item><c>You may have this land enter tapped</c> is untapped.</item>
/// </list>
/// A land face with no such sentence is untapped: "untapped" is a different
/// word. Any other sentence with both "enter" and "tapped", two states on
/// one face, or two land faces that disagree is unmatched: no facet, and
/// the caller reports it.
/// </summary>
public static partial class SourceProfileDerivation
{
    /// <summary>Fetchable land types; empty = any land within the Basic
    /// constraint. <paramref name="Activation"/> is the found land's state
    /// when every search clause is a land's closed tap-and-sacrifice
    /// activation (<see cref="ParseActivation"/>); null otherwise.</summary>
    public sealed record Fetch(string[] LandTypes, bool BasicOnly, FetchActivation? Activation);

    public sealed record Result(
        string[]? ProducedColors,
        Fetch? Fetch,
        bool UnmatchedSearchLand,
        bool IdentityBound,
        bool OpponentDependent,
        bool UnmatchedOpponentSource,
        bool TokenToAnotherPlayer);

    private const string SearchPhrase = "search your library for ";

    public static Result Derive(string[]? producedMana, string? oracleText)
    {
        var (fetch, unmatched) = ParseFetch(oracleText);
        var produced = producedMana is { Length: > 0 } ? producedMana : null;
        var (opponentDependent, unmatchedOpponent, tokenToAnotherPlayer) = OpponentDependence(oracleText, hasProducedMana: produced is not null, hasFetch: fetch is not null);
        return new Result(produced, fetch, unmatched, IsIdentityBound(produced, oracleText), opponentDependent, unmatchedOpponent, tokenToAnotherPlayer);
    }

    private enum Dependence
    {
        Independent,
        Dependent,
        Unmatched,
        TokenToAnotherPlayer,
    }

    /// <summary>
    /// Whether every mana-producing ability and every
    /// fetch ability depends on another player or on combat. A
    /// mana-producing ability adds mana or creates a mana token. A fetch
    /// ability holds a search clause the fetch parse matched. Each mode line
    /// ("• Create a Treasure token.") is its own ability, read with the line
    /// above it that holds the trigger. Reminder text belongs to its
    /// ability: a token's mana ability or fetch ability marks the ability
    /// that creates the token. The dependence parse reads the reminder text
    /// only when the ability is a source through it alone (a token, or
    /// firebending's attack trigger); elsewhere it explains a keyword. A gift
    /// goes to an opponent, so Gift a Treasure never gives the card its own mana.
    /// A token ability creates a token for another player when
    /// its subject is "its controller", "target opponent", "an opponent" or
    /// "each opponent", or "they" after an opponent is named. A gift does
    /// the same. When every source ability does this, the card is a token
    /// for another player. When some source abilities do this, the others
    /// depend on another player, and none is independent, the card is
    /// unmatched.
    /// </summary>
    /// <remarks>
    /// The closed template family has four shapes. The first is a trigger on
    /// an opponent's action or on a creature an opponent controls. The second
    /// is a trigger on combat damage. The third is an amount that counts what
    /// opponents control, hold or lose. The fourth is a condition that an
    /// opponent controls more than you. An ability that mentions an opponent,
    /// combat or an attack outside the family is unmatched. The card then
    /// stays a source and gets a report entry: the parse never guesses.
    /// </remarks>
    private static (bool Dependent, bool Unmatched, bool TokenToAnotherPlayer) OpponentDependence(string? oracleText, bool hasProducedMana, bool hasFetch)
    {
        var abilities = new List<string>();
        var header = "";
        foreach (var line in (oracleText ?? "").Split('\n'))
        {
            if (line.StartsWith('•') && abilities.Count > 0)
            {
                // The first mode replaces its bare header; later modes add to it.
                if (abilities[^1] == header)
                {
                    abilities.RemoveAt(abilities.Count - 1);
                }

                abilities.Add(header + "\n" + line);
            }
            else
            {
                header = line;
                abilities.Add(line);
            }
        }

        bool IsSource(string ability)
            => (hasProducedMana && ProducesMana().IsMatch(ability))
                || (hasFetch && SearchClauses(ability).Any(clause => clause.Targets is not null));

        static bool IsGift(string ability) => ability.StartsWith("Gift ", StringComparison.Ordinal);

        // A gift goes to an opponent: it is a source that gives its token away.
        var sources = abilities
            .Select(ability =>
            {
                if (IsGift(ability))
                {
                    return IsSource(ability) ? Dependence.TokenToAnotherPlayer : (Dependence?)null;
                }

                var rules = ReminderText().Replace(ability, "");
                var source = IsSource(rules) ? rules : IsSource(ability) ? ability : null;
                return source is null ? null
                    : GivesTokenToAnotherPlayer(source, IsSource) ? Dependence.TokenToAnotherPlayer : DependenceOf(source);
            })
            .OfType<Dependence>()
            .ToList();
        if (sources.Count == 0)
        {
            return (false, false, false);
        }

        var mixed = !sources.Contains(Dependence.Independent)
            && sources.Contains(Dependence.Dependent) && sources.Contains(Dependence.TokenToAnotherPlayer);
        return (
            sources.All(d => d == Dependence.Dependent),
            sources.Contains(Dependence.Unmatched) || mixed,
            sources.All(d => d == Dependence.TokenToAnotherPlayer));
    }

    /// <summary>
    /// The ability creates a mana token for another player.
    /// Without those sentences, the ability is not a source.
    /// </summary>
    private static bool GivesTokenToAnotherPlayer(string ability, Func<string, bool> isSource)
    {
        var sentences = SentenceBreak().Split(ability).Where(sentence => sentence.Length > 0).ToList();
        var kept = new List<string>();
        var tokenToAnotherPlayer = false;
        for (var i = 0; i < sentences.Count; i++)
        {
            var token = TokenCreation().Match(sentences[i]);
            if (token.Success && CreateVerb().Count(sentences[i]) == 1
                && IsOtherPlayer(token.Groups["subject"].Value, string.Join(' ', sentences.Take(i).Append(token.Groups["subject"].Value))))
            {
                tokenToAnotherPlayer = true;
            }
            else
            {
                kept.Add(sentences[i]);
            }
        }

        return tokenToAnotherPlayer && !isSource(string.Join(' ', kept));
    }

    private static bool IsOtherPlayer(string subject, string named)
        => OtherPlayerSubject().IsMatch(subject)
            || (TheySubject().IsMatch(subject) && Opponent().IsMatch(named));

    private static Dependence DependenceOf(string ability)
    {
        if (OpponentTrigger().IsMatch(ability) || CombatDamageTrigger().IsMatch(ability)
            || OpponentAmount().IsMatch(ability) || ControlsMoreThanYou().IsMatch(ability))
        {
            return Dependence.Dependent;
        }

        // An identity-bound clause (Fellwar Stone) mentions an opponent's
        // lands; tier 1 clips it to the deck's color identity instead.
        return OpponentOrCombat().IsMatch(BoundClause().Replace(ability, ""))
            ? Dependence.Unmatched
            : Dependence.Independent;
    }

    private static readonly string[] FiveColors = ["W", "U", "B", "R", "G"];

    /// <summary>
    /// The card produces all five colors. Every "Add … any
    /// color" ability binds them to the commander's color identity or to
    /// lands the players control (Command Tower, Exotic Orchard). One
    /// unbound any-color ability (Plaza of Heroes) leaves the card unbound,
    /// and so does a card with none (a Treasure maker's quoted ability).
    /// </summary>
    public static bool IsIdentityBound(string[]? producedMana, string? oracleText)
    {
        if (producedMana is null || !FiveColors.All(producedMana.Contains))
        {
            return false;
        }

        var anyColor = AnyColorClause().Matches(NotRulesText().Replace(oracleText ?? "", "")).ToList();
        return anyColor.Count > 0 && anyColor.All(clause => BoundClause().IsMatch(clause.Value));
    }

    /// <summary>The colors a land face makes: the
    /// <paramref name="producedMana"/> symbols its own "Add" text names, in
    /// union order. Null when it names none: the caller keeps the union
    /// and reports the face.</summary>
    public static string[]? FaceColors(string[] producedMana, string? faceText)
    {
        var named = AddClause().Matches(faceText ?? "")
            .SelectMany(clause => ColorSymbol().Matches(clause.Value))
            .Select(symbol => symbol.Groups["color"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var colors = producedMana.Where(named.Contains).ToArray();
        return colors.Length > 0 ? colors : null;
    }

    public static bool IsLandFace(string typeLine) => LandType().IsMatch(typeLine);

    /// <summary>One face as the enters-tapped parse sees it.</summary>
    public sealed record Face(string Name, string TypeLine, string? OracleText);

    /// <summary>The faces the enters-tapped parse reads: the card's faces,
    /// or the card itself as its own one face when it has none.</summary>
    public static IReadOnlyList<Face> FacesOf(string name, string typeLine, string? oracleText, IReadOnlyList<Face> faces)
        => faces.Count > 0 ? faces : [new Face(name, typeLine, oracleText)];

    /// <summary><paramref name="Land"/> is whether any face is a land: the
    /// one land test the profile phase and this parse share.</summary>
    public sealed record EntersTappedResult(EntersTappedState? State, bool Unmatched, bool Land);

    /// <summary>The enters-tapped state over the faces whose type line
    /// carries Land; a card with no land face has no state.</summary>
    public static EntersTappedResult EntersTapped(IEnumerable<Face> faces)
    {
        var land = false;
        EntersTappedState? state = null;
        foreach (var face in faces.Where(f => IsLandFace(f.TypeLine)))
        {
            land = true;
            var faceState = FaceEntersTapped(face);
            if (faceState is null || (state is not null && state != faceState))
            {
                return new EntersTappedResult(null, Unmatched: true, Land: true);
            }

            state = faceState;
        }

        return new EntersTappedResult(state, Unmatched: false, land);
    }

    /// <summary>One land face's state, or null when any sentence is unmatched.</summary>
    private static EntersTappedState? FaceEntersTapped(Face face)
    {
        var text = SelfName(face.Name).Replace(NotRulesText().Replace(face.OracleText ?? "", ""), "this land");
        var sentences = SentenceBreak().Split(text)
            .Select(sentence => sentence.Trim().TrimEnd('.'))
            .Where(sentence => sentence.Length > 0)
            .ToList();
        EntersTappedState? state = null;
        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            if (!EnterWord().IsMatch(sentence) || !TappedWord().IsMatch(sentence))
            {
                continue;
            }

            var previous = i > 0 ? sentences[i - 1] : "";
            var parsed = ParseEntersTapped(sentence, previous);
            if (parsed is null || (state is not null && state != parsed))
            {
                return null;
            }

            state = parsed;
        }

        return state ?? EntersTappedState.Untapped;
    }

    private static EntersTappedState? ParseEntersTapped(string sentence, string previous)
    {
        if (TappedSentence().IsMatch(sentence))
        {
            return EntersTappedState.Tapped;
        }

        if (ChoiceSentence().IsMatch(sentence))
        {
            if (PayLifeChoice().IsMatch(previous))
            {
                return EntersTappedState.Untapped;
            }

            return PlayerChoice().IsMatch(previous) ? EntersTappedState.Conditional : null;
        }

        if (ConditionalSentence().IsMatch(sentence))
        {
            return EntersTappedState.Conditional;
        }

        if (MayEnterTappedSentence().IsMatch(sentence))
        {
            return EntersTappedState.Untapped;
        }

        return null;
    }

    /// <summary>The names a land face's text can call itself: its name, the
    /// name without an Alchemy "A-" prefix, and the name before a ", title"
    /// suffix ("Boseiju, Who Shelters All"). The full name always stays in
    /// the set, so a name that really starts with "A-" still matches.</summary>
    internal static Regex SelfName(string name)
    {
        var bare = name.StartsWith("A-", StringComparison.Ordinal) ? name[2..] : name;
        var comma = bare.IndexOf(", ", StringComparison.Ordinal);
        var names = new[] { name, bare, comma > 0 ? bare[..comma] : bare }.Distinct();
        return new Regex(@"\b(?:" + string.Join('|', names.Select(Regex.Escape)) + @")\b", RegexOptions.IgnoreCase);
    }

    /// <summary>Whole-card text, faces included, for the fetch parse.
    /// Reminder text stays here: a landcycling keyword's parenthesized search
    /// text is the card's fetch ability, and erasing it would silently drop
    /// a search-land. The enters-tapped parse takes the opposite policy in
    /// <see cref="NotRulesText"/>; the two parses do not share text.</summary>
    public static string CombinedText(IEnumerable<string?> texts)
        => string.Join('\n', texts.Where(text => text is not null));

    private static (Fetch? Fetch, bool Unmatched) ParseFetch(string? text)
    {
        if (text is null)
        {
            return (null, false);
        }

        var targets = new List<Target>();
        var activations = new List<FetchActivation?>();
        foreach (var (at, clause, parsed, end) in SearchClauses(text))
        {
            if (parsed is null)
            {
                if (IsLandSearch(clause))
                {
                    return (null, true);
                }
            }
            else
            {
                targets.AddRange(parsed);
                activations.Add(ParseActivation(text[..at], clause[end..]));
            }
        }

        if (targets.Count == 0)
        {
            return (null, false);
        }

        // The flat profile can't express per-target Basic constraints
        // ("a basic land card and a Gate card") — unmatched, not a guess.
        var basicOnly = targets[0].Basic;
        if (targets.Any(t => t.Basic != basicOnly))
        {
            return (null, true);
        }

        var types = targets.Any(t => t.Types.Length == 0)
            ? []
            : targets.SelectMany(t => t.Types).Distinct().ToArray();
        var activation = activations.Distinct().Count() == 1 ? activations[0] : null;
        return (new Fetch(types, basicOnly, activation), false);
    }

    private sealed record Target(bool Basic, string[] Types);

    /// <summary>Every "search your library for" clause in the text, with its
    /// parsed targets (null outside the template family) and where they end.</summary>
    private static IEnumerable<(int At, string Clause, List<Target>? Targets, int End)> SearchClauses(string text)
    {
        for (var at = text.IndexOf(SearchPhrase, StringComparison.OrdinalIgnoreCase);
            at >= 0;
            at = text.IndexOf(SearchPhrase, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var clause = text[(at + SearchPhrase.Length)..];
            var targets = ParseTargets(clause, out var end);
            yield return (at, clause, targets, end);
        }
    }

    /// <summary>The and-joined targets at the head of a search clause, or
    /// null when the head falls outside the template family.
    /// <paramref name="end"/> is where the targets stop.</summary>
    private static List<Target>? ParseTargets(string clause, out int end)
    {
        var targets = new List<Target>();
        end = 0;
        while (true)
        {
            var match = TargetPattern().Match(clause, end);
            if (!match.Success)
            {
                return null;
            }

            targets.Add(new Target(
                match.Groups["basic"].Success,
                [.. match.Groups["type"].Captures.Select(c => c.Value).Distinct()]));
            end = match.Index + match.Length;
            if (!clause.AsSpan(end).StartsWith(" and "))
            {
                // Anything but a clean clause ending ("and/or …", "each …")
                // is outside the family — the whole clause stays unmatched
                // rather than silently dropping the tail.
                return end == clause.Length || clause[end] is ',' or '.' or ';' or ':' or '\n'
                    ? targets
                    : null;
            }

            end += " and ".Length;
        }
    }

    /// <summary>The found land's state when the clause is a land's closed
    /// fetch activation: the line opens <c>{T}, [Pay N life, ]Sacrifice this
    /// land:</c> and the targets end <c>, put it onto the battlefield[ tapped],
    /// then shuffle.</c> Any other shape (a mana cost, a trigger, a search to
    /// hand, a spell) is null: the profile never guesses an ability.</summary>
    private static FetchActivation? ParseActivation(string before, string after)
    {
        if (!ActivationCost().IsMatch(before[(before.LastIndexOf('\n') + 1)..]))
        {
            return null;
        }

        var outcome = ActivationOutcome().Match(after);
        if (!outcome.Success)
        {
            return null;
        }

        return outcome.Groups["tapped"].Success ? FetchActivation.Tapped : FetchActivation.Untapped;
    }

    /// <summary>Whether an unparsed search clause targets lands at all —
    /// the line between "unmatched search-land" (reported) and an ordinary
    /// tutor (no profile, no report). Judged on the rest of the ability's
    /// line: a later sentence can carry the land condition ("If it's a land
    /// card, …"), while other abilities mention lands incidentally.</summary>
    private static bool IsLandSearch(string clause)
    {
        var end = clause.IndexOf('\n');
        return LandWord().IsMatch(end >= 0 ? clause[..end] : clause);
    }

    [GeneratedRegex(
        @"\G(?:an?|up to (?:one|two|three|four|five|six|seven|eight|nine|ten|X)) (?<basic>basic )?"
        + @"(?:land|(?:(?<type>Plains|Island|Swamp|Mountain|Forest|Gate|Desert)(?:, or | or |, ))*"
        + @"(?<type>Plains|Island|Swamp|Mountain|Forest|Gate|Desert)) cards?(?!\w)")]
    private static partial Regex TargetPattern();

    [GeneratedRegex(@"\b(?:lands?|Plains|Islands?|Swamps?|Mountains?|Forests?|Gates?|Deserts?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LandWord();

    [GeneratedRegex(@"^\{T\}, (?:Pay \d+ life, )?Sacrifice this land: $")]
    private static partial Regex ActivationCost();

    [GeneratedRegex(@"^, put it onto the battlefield(?<tapped> tapped)?, then shuffle\.")]
    private static partial Regex ActivationOutcome();

    [GeneratedRegex(@"\bLand\b")]
    private static partial Regex LandType();

    /// <summary>A mana ability's effect, up to the end of its sentence.</summary>
    [GeneratedRegex(@"\bAdd\b[^.\n]*")]
    private static partial Regex AddClause();

    /// <summary>An "Add … any color" effect, up to the end of its sentence:
    /// "any type", "any one color", and "any combination of colors" too.</summary>
    [GeneratedRegex(@"\bAdd\b[^.\n]*\bany (?:one )?(?:color|type|combination)\b[^.\n]*")]
    private static partial Regex AnyColorClause();

    /// <summary>The bound any-color phrases: the commander's color identity,
    /// or lands (or Gates) the players control.</summary>
    [GeneratedRegex(@"\bany (?:color|type) (?:in your commander's color identity|that a (?:basic )?(?:land|Gate) (?:you control|an opponent controls) could produce)\b")]
    private static partial Regex BoundClause();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ReminderText();

    /// <summary>A sentence that creates a mana token; the subject is what
    /// precedes the verb.</summary>
    [GeneratedRegex(@"^(?<subject>.*?)\bcreates?\b[^.\n]*\b(?:Treasure|Gold|Powerstone)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TokenCreation();

    /// <summary>One verb: a sentence with a second one may make a token for you.</summary>
    [GeneratedRegex(@"\bcreates?\b", RegexOptions.IgnoreCase)]
    private static partial Regex CreateVerb();

    [GeneratedRegex(@"\b(?:its controller|target opponent|an opponent|each opponent)(?:\s+(?:then|also|may))*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OtherPlayerSubject();

    [GeneratedRegex(@"\bthey\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TheySubject();

    [GeneratedRegex(@"\bopponents?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Opponent();

    /// <summary>Adds mana, or creates a mana token.</summary>
    [GeneratedRegex(
        @"\badds?\b[^.\n]*?(?:\{[WUBRGC]\}|\bmana\b)|\bcreates?\b[^.\n]*\b(?:Treasure|Gold|Powerstone)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProducesMana();

    [GeneratedRegex(@"\bWhenever (?:an opponent|a (?:nontoken )?creature an opponent controls)\b")]
    private static partial Regex OpponentTrigger();

    [GeneratedRegex(@"\bWhenever [^,.]*\bdeals? combat damage\b")]
    private static partial Regex CombatDamageTrigger();

    /// <summary>An amount that counts what opponents control, hold or lose:
    /// "where X is the number of artifacts and enchantments your opponents
    /// control", "for each card in target opponent's hand".</summary>
    [GeneratedRegex(
        @"\b(?:where X is the number of|[Ff]or each|equal to the number of)\b[^.]*?"
        + @"\b(?:(?:your opponents|target opponent) controls?|target opponent's|your opponents have lost)\b")]
    private static partial Regex OpponentAmount();

    /// <summary>A condition that an opponent controls more than you: "if an
    /// opponent controls more lands than you", "Activate only if …", "that
    /// player" and "defending player", and "the number of players who control
    /// [at least two] more lands than you".</summary>
    [GeneratedRegex(@"\b(?:an opponent|that player|defending player|players who) controls? (?:at least \w+ )?more\b[^.\n]*\bthan you\b", RegexOptions.IgnoreCase)]
    private static partial Regex ControlsMoreThanYou();

    [GeneratedRegex(@"\bopponents?\b|\bcombat\b|\battack(?:s|ing)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpponentOrCombat();

    [GeneratedRegex(@"\{(?<color>[WUBRGC])\}")]
    private static partial Regex ColorSymbol();

    /// <summary>Reminder text and quoted granted-ability text, which the
    /// enters-tapped parse and <see cref="CardFeatureDerivation"/> remove.
    /// The fetch parse keeps reminder text: see <see cref="CombinedText"/>.</summary>
    [GeneratedRegex(@"\([^)]*\)|""[^""]*""")]
    internal static partial Regex NotRulesText();

    /// <summary>A sentence ends at a period or at a line break: a keyword
    /// line ("Hexproof", "Hideaway 4") carries no period.</summary>
    [GeneratedRegex(@"(?<=\.)\s+|\n")]
    internal static partial Regex SentenceBreak();

    [GeneratedRegex(@"\benters?\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnterWord();

    [GeneratedRegex(@"\btapped\b", RegexOptions.IgnoreCase)]
    private static partial Regex TappedWord();

    [GeneratedRegex(@"^this land enters (?:the battlefield )?tapped(?: with .*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TappedSentence();

    /// <summary>The outcome of a declined player choice; "it" is the one
    /// place the corpus uses a pronoun for the land itself.</summary>
    [GeneratedRegex(@"^if you don't, (?:this land|it) enters (?:the battlefield )?tapped$", RegexOptions.IgnoreCase)]
    private static partial Regex ChoiceSentence();

    [GeneratedRegex(
        @"^this land enters (?:the battlefield )?tapped (?:unless|if) .*$"
        + @"|^(?:if|during|unless) [^,]*, this land enters (?:the battlefield )?tapped(?: (?:unless|if) .*)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConditionalSentence();

    [GeneratedRegex(@"^you may have this land enter (?:the battlefield )?tapped\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex MayEnterTappedSentence();

    /// <summary>The choice a shock land offers; the declined outcome that
    /// follows it is untapped, because the cost is always payable.</summary>
    [GeneratedRegex(@"\byou may pay (?:\d+|X) life$", RegexOptions.IgnoreCase)]
    private static partial Regex PayLifeChoice();

    /// <summary>Any other offered choice (reveal, behold): the declined
    /// outcome is conditional on game state.</summary>
    [GeneratedRegex(@"\byou may \w", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerChoice();
}
