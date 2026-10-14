using System.Text.RegularExpressions;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Ingest;

/// <summary>
/// Derives the response feature of one card face (ADR 0013) from the rules
/// text that <see cref="CardFeatureDerivation"/> reads: reminder text and
/// quoted granted-ability text removed. It reads each sentence against
/// closed families and never guesses.
/// <list type="bullet">
/// <item>Counter families, in the rules text of an instant or sorcery or in
/// its first mode: <c>Counter target spell</c> (any spell);
/// <c>… noncreature spell</c> (an excluded type); <c>… instant or sorcery
/// spell</c> (a type list); <c>… blue spell</c> and <c>… spell if it's
/// blue</c> (a color); <c>… creature spell</c> (creature spells only);
/// <c>… activated or triggered ability</c> (an ability). A creature-only or
/// an ability counter is stored and reaches no disruption card, so it is
/// not unprofiled. "You don't control" after the spell keeps the reach,
/// because the scripted opponent controls the disruption card. A trailing
/// ", activated ability, or triggered ability" keeps the spell reach
/// (Disallow).</item>
/// <item>Modifiers: <c>unless its controller pays {N}</c> sets the amount.
/// The pact clause "If you don't, you lose the game" anywhere on the face
/// sets the game-loss flag. A side effect in another sentence (Arcane
/// Denial's draws, An Offer You Can't Refuse's Treasures, Mana Drain's mana)
/// leaves the counter as it is.</item>
/// <item>Protection families, in the same readable sentences: a one-shot
/// "can't be countered" grant to our spells; a team grant until end of
/// turn; a target grant, with the "It gains …" follow-up that refers back to
/// the target; and phase-out (see <c>GrantsOf</c>). Each keyword of a grant
/// is one <see cref="ProtectionGrant"/>, in text order. A static or attached
/// grant ("Equipped creature has shroud", "Spells you control can't be
/// countered" on a permanent) gets no grant and is not unprofiled, because
/// XMage enforces it.</item>
/// </list>
/// The derivation reads only the first mode of a modal card. A sentence
/// that mentions an answer (the response mention set: "counter target",
/// "counter that", "counter it" and the like) and that no family matches
/// makes the face response-unprofiled: a later mode, a triggered or
/// activated ability, or a qualifier outside the families ("legendary
/// spell", "with mana value 4 or greater", "protection from the color of
/// your choice", a grant under a condition). The mention set also holds the
/// protection mentions of <c>GrantMention</c>. The first counter in text order
/// is the counter. The add, draw and search mentions of
/// <see cref="CardFeatureDerivation"/> do not read this set, so its
/// unprofiled flag does not change.
/// </summary>
public static partial class ResponseDerivation
{
    private static readonly Dictionary<string, string> ColorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["white"] = "W",
        ["blue"] = "U",
        ["black"] = "B",
        ["red"] = "R",
        ["green"] = "G",
    };

    private static readonly string[] CardTypes =
        ["Artifact", "Battle", "Creature", "Enchantment", "Instant", "Kindred", "Land", "Planeswalker", "Sorcery"];

    /// <summary>The face's response feature, null when it has none, and the
    /// first sentence that mentions an answer outside the families.</summary>
    public sealed record Result(ResponseFeature? Response, string? UnmatchedSentence);

    public static Result Derive(CardFeatureDerivation.Face face, string rulesText)
    {
        CounterFeature? counter = null;
        var grants = new List<ProtectionGrant>();
        string? unmatched = null;
        var seenMode = false;
        foreach (var raw in rulesText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            bool readable;
            var isStatic = false;
            string body;
            if (line.StartsWith("• ", StringComparison.Ordinal))
            {
                readable = face.IsSpell && !seenMode;
                seenMode = true;
                body = line[2..];
            }
            else
            {
                var ability = CardFeatureDerivation.Ability.Of(line, face.IsSpell)!;
                readable = ability.Kind == CardFeatureDerivation.AbilityKind.Spell;
                isStatic = ability.Kind == CardFeatureDerivation.AbilityKind.Other;
                body = ability.Effect;
            }

            var sentences = SourceProfileDerivation.SentenceBreak().Split(body)
                .Select(sentence => sentence.Trim().TrimEnd('.').TrimEnd())
                .Where(sentence => sentence.Length > 0);

            // The target an "It …" follow-up sentence refers back to: the
            // last target creature or permanent of this line.
            string? antecedent = null;
            foreach (var sentence in sentences)
            {
                if (CounterMention().IsMatch(sentence))
                {
                    if (readable && CounterOf(sentence) is { } read)
                    {
                        counter ??= read;
                        continue;
                    }

                    unmatched ??= sentence;
                    continue;
                }

                if (!GrantMention().IsMatch(sentence))
                {
                    antecedent = TargetScopeOf(sentence) ?? antecedent;
                    continue;
                }

                // A static or attached grant: XMage enforces it with no
                // pilot decision.
                if (isStatic)
                {
                    continue;
                }

                if (readable && GrantsOf(sentence, antecedent) is { } stated)
                {
                    grants.AddRange(stated);
                    antecedent = TargetScopeOf(sentence) ?? antecedent;
                    continue;
                }

                unmatched ??= sentence;
            }
        }

        if (counter is not null && PactClause().IsMatch(rulesText))
        {
            counter = counter with { LosesGame = true };
        }

        return new Result(counter is null && grants.Count == 0 ? null : new ResponseFeature(counter, grants), unmatched);
    }

    /// <summary>
    /// The grants one sentence states; null outside the protection
    /// families:
    /// <list type="bullet">
    /// <item>"Spells you control can't be countered this turn".</item>
    /// <item>A team grant until end of turn: "Creatures you control gain …",
    /// "[You and ]permanents you control [also ]gain …".</item>
    /// <item>A target grant until end of turn: "Target creature [you
    /// control] [gets +N/+N and ]gains …", or "target permanent" or
    /// "target nonland permanent"; and the follow-up "It gains …" after a
    /// sentence that names the target.</item>
    /// <item>Phase-out: "[All ]permanents you control phase out",
    /// "Creatures you control phase out", "Target creature [you control]
    /// phases out" (or permanent), and the follow-up "It phases out".</item>
    /// </list>
    /// </summary>
    private static List<ProtectionGrant>? GrantsOf(string sentence, string? antecedent)
    {
        if (CantBeCounteredSentence().IsMatch(sentence))
        {
            return [new ProtectionGrant(ProtectionGrant.CantBeCountered, ProtectionGrant.Spells)];
        }

        if (PhaseOutSentence().Match(sentence) is { Success: true } phase)
        {
            return ScopeOf(phase.Groups["who"].Value, antecedent) is { } scope
                ? [new ProtectionGrant(ProtectionGrant.PhaseOut, scope)]
                : null;
        }

        if (GainSentence().Match(sentence) is not { Success: true } gain
            || ScopeOf(gain.Groups["who"].Value, antecedent) is not { } who
            || KeywordsOf(gain.Groups["what"].Value) is not { Count: > 0 } keywords)
        {
            return null;
        }

        return [.. keywords.Select(keyword => new ProtectionGrant(keyword, who))];
    }

    /// <summary>The scope of a grant's subject; null outside the
    /// families.</summary>
    private static string? ScopeOf(string who, string? antecedent) => who.ToLowerInvariant() switch
    {
        "it" => antecedent,
        "creatures you control" => ProtectionGrant.Creatures,
        "permanents you control" or "all permanents you control" or "you and permanents you control" => ProtectionGrant.Permanents,
        _ => TargetScopeOf(who),
    };

    /// <summary>The scope of the last target creature or permanent a
    /// sentence names, unless another player controls it; null for
    /// none.</summary>
    private static string? TargetScopeOf(string sentence)
        => TargetPhrase().Matches(sentence) is { Count: > 0 } matches
            ? matches[^1].Groups["what"].Value == "creature" ? ProtectionGrant.TargetCreature : ProtectionGrant.TargetPermanent
            : null;

    /// <summary>The protection keywords of a list such as "hexproof and
    /// indestructible" or "hexproof from blue and from black". Evasion and
    /// combat keywords in the list grant nothing to answer with and are
    /// skipped. Null for any other word, and for "protection from the color
    /// of your choice", whose color XMage chooses.</summary>
    private static List<string>? KeywordsOf(string list)
    {
        var keywords = new List<string>();
        string? lastFrom = null;
        foreach (var part in KeywordBreak().Split(list))
        {
            var word = part.Trim();
            if (word is ProtectionGrant.Hexproof or ProtectionGrant.Shroud or ProtectionGrant.Indestructible)
            {
                keywords.Add(word);
                lastFrom = null;
                continue;
            }

            if (OtherKeywords.Contains(word))
            {
                lastFrom = null;
                continue;
            }

            var from = FromPhrase().Match(word);
            if (!from.Success)
            {
                return null;
            }

            lastFrom = from.Groups["kind"].Success ? from.Groups["kind"].Value : lastFrom;
            if (lastFrom is null || FromKeyword(lastFrom, from.Groups["what"].Value) is not { } keyword)
            {
                return null;
            }

            keywords.Add(keyword);
        }

        return keywords;
    }

    private static string? FromKeyword(string kind, string what)
    {
        if (ColorWords.TryGetValue(what, out var color))
        {
            return kind == "hexproof" ? ProtectionGrant.HexproofFrom(color) : ProtectionGrant.ProtectionFrom(color);
        }

        return (kind, what) switch
        {
            ("protection", "everything") => ProtectionGrant.ProtectionFromEverything,
            ("protection", "all colors" or "each color") => ProtectionGrant.ProtectionFromAllColors,
            _ => null,
        };
    }

    private static readonly HashSet<string> OtherKeywords = new(StringComparer.Ordinal)
    {
        "flying", "trample", "haste", "lifelink", "vigilance", "first strike", "double strike", "deathtouch", "menace", "reach",
    };

    /// <summary>The counter one sentence states; null outside the
    /// families.</summary>
    private static CounterFeature? CounterOf(string sentence)
    {
        if (CounterSentence().Match(sentence) is not { Success: true } match)
        {
            return null;
        }

        var pays = match.Groups["pays"].Success ? match.Groups["pays"].Value : null;
        var what = match.Groups["what"].Value;
        if (AbilityTarget().IsMatch(what))
        {
            return new CounterFeature(CounterReach.Ability, [], [], [], pays, false);
        }

        what = SpellSuffix().Replace(what, "");
        if (SpellIfColor().Match(what) is { Success: true } ifColor)
        {
            return Spell(colors: [ColorWords[ifColor.Groups["color"].Value]]);
        }

        if (SpellPhrase().Match(what) is not { Success: true } phrase)
        {
            return null;
        }

        var types = new List<string>();
        var excluded = new List<string>();
        var colors = new List<string>();
        var words = phrase.Groups["words"].Value;
        foreach (var word in words.Length == 0 ? [] : ListBreak().Split(words))
        {
            if (ColorWords.TryGetValue(word, out var color))
            {
                colors.Add(color);
            }
            else if (TypeOf(word) is { } type)
            {
                types.Add(type);
            }
            else if (word.StartsWith("non", StringComparison.Ordinal) && TypeOf(word[3..]) is { } nonType)
            {
                excluded.Add(nonType);
            }
            else
            {
                return null;
            }
        }

        // One qualifier kind per counter: "blue creature spell" or
        // "noncreature, nonland spell" with a type list is outside the
        // families.
        if (new[] { types.Count, excluded.Count, colors.Count }.Count(count => count > 0) > 1)
        {
            return null;
        }

        return Spell(types, excluded, colors);

        CounterFeature Spell(List<string>? types = null, List<string>? excluded = null, List<string>? colors = null)
            => new(CounterReach.Spell, types ?? [], excluded ?? [], colors ?? [], pays, false);
    }

    private static string? TypeOf(string word)
        => CardTypes.FirstOrDefault(type => type.Equals(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>The response mention set: a sentence that counters
    /// something. "+1/+1 counter on" and "can't be countered" are not in the
    /// set.</summary>
    [GeneratedRegex(@"\bcounter (?:target|that|it|them|all|each|up to|any number of)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CounterMention();

    /// <summary>"Counter target …[ unless its controller pays {N}]".</summary>
    [GeneratedRegex(@"^Counter target (?<what>.+?)(?: unless its controller pays (?<pays>(?:\{[0-9WUBRGCX]+\})+))?$")]
    private static partial Regex CounterSentence();

    [GeneratedRegex(@"^(?:activated or triggered ability|activated ability|triggered ability|ability)$")]
    private static partial Regex AbilityTarget();

    /// <summary>Words after "spell" that keep the reach: the opponent
    /// controls the disruption card, and an ability target adds no spell
    /// reach of its own.</summary>
    [GeneratedRegex(@"(?<=\bspell)(?: you don't control|, activated ability, or triggered ability)$")]
    private static partial Regex SpellSuffix();

    [GeneratedRegex(@"^spell if it's (?<color>white|blue|black|red|green)$")]
    private static partial Regex SpellIfColor();

    /// <summary>"[qualifier list ]spell". The list holds card types, colors
    /// or "non" types, joined by ", ", " or " or ", or ".</summary>
    [GeneratedRegex(@"^(?:(?<words>[a-z]+(?:(?:, or |, | or )[a-z]+)*) )?spell$")]
    private static partial Regex SpellPhrase();

    [GeneratedRegex(@", or |, | or ")]
    private static partial Regex ListBreak();

    /// <summary>The protection part of the response mention set: a sentence
    /// that grants hexproof, shroud, indestructible or protection to
    /// something other than the player alone ("you gain protection from
    /// everything" protects no spell and no permanent), a phase-out, or a
    /// "can't be countered" grant to our spells. "This spell can't be
    /// countered" and "that spell can't be countered" (a mana rider) are not
    /// in the set.</summary>
    [GeneratedRegex(@"(?<!\byou )\bgains? (?:[^.]*? )?(?:hexproof|shroud|indestructible|protection)\b|\bphases? out\b|\bspells you control can't be countered\b|\bcan't be countered this turn\b|\btarget spell can't be countered\b", RegexOptions.IgnoreCase)]
    private static partial Regex GrantMention();

    [GeneratedRegex(@"^Spells you control can't be countered this turn$")]
    private static partial Regex CantBeCounteredSentence();

    /// <summary>"&lt;who&gt; phase(s) out".</summary>
    [GeneratedRegex(@"^(?<who>It|All permanents you control|Permanents you control|Creatures you control|Target (?:creature|permanent|nonland permanent)(?: you control)?) phases? out$")]
    private static partial Regex PhaseOutSentence();

    /// <summary>"&lt;who&gt; [also ][gets +N/+N and ]gain(s) &lt;keywords&gt; until end of turn".</summary>
    [GeneratedRegex(@"^(?<who>It|Creatures you control|Permanents you control|You and permanents you control|Target (?:creature|permanent|nonland permanent)(?: you control)?) (?:also )?(?:gets [+-]\d+/[+-]\d+ and )?gains? (?<what>.+) until end of turn$")]
    private static partial Regex GainSentence();

    /// <summary>"target creature" or "target [nonland ]permanent", not one
    /// another player controls.</summary>
    [GeneratedRegex(@"\btarget (?<what>creature|permanent|nonland permanent)\b(?! an opponent controls| you don't control| your opponents control)", RegexOptions.IgnoreCase)]
    private static partial Regex TargetPhrase();

    [GeneratedRegex(@", and |, | and ")]
    private static partial Regex KeywordBreak();

    /// <summary>"protection from black", "hexproof from blue", or a bare
    /// "from black" that repeats the kind before it.</summary>
    [GeneratedRegex(@"^(?:(?<kind>protection|hexproof) )?from (?<what>everything|all colors|each color|white|blue|black|red|green)$")]
    private static partial Regex FromPhrase();

    /// <summary>The pact clause (Pact of Negation): a later payment, or its
    /// controller loses the game.</summary>
    [GeneratedRegex(@"\bIf you don't, you lose the game\b")]
    private static partial Regex PactClause();
}
