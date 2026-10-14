using System.Text.Json;
using MtgArchive.Ingest;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// A card feature profile as one line of its features, defaults left out
/// ("develops tap={C}{C} activated"). A table-driven test states a whole
/// expected profile in one string, and a failure shows both lines. A tutor
/// reads "tutor=destination[+tapped]/via[/cost][/delay=N]" with wire names.
/// </summary>
internal static class ProfileText
{
    public static string Of(CardFeatureProfile profile)
    {
        var parts = new List<string>();
        if (profile.Develops)
        {
            parts.Add("develops");
        }

        if (profile.Draws)
        {
            parts.Add("draws");
        }

        if (profile.TapMana.Count > 0)
        {
            parts.Add("tap=" + string.Join('|', profile.TapMana));
        }

        if (profile.HandMana.Count > 0)
        {
            parts.Add("hand=" + string.Join('|', profile.HandMana));
        }

        if (profile.SpellMana is { } spell)
        {
            parts.Add("spell=" + spell);
        }

        if (profile.Tutor is { } tutor)
        {
            var tapped = tutor.EntersTapped ? "+tapped" : "";
            var cost = tutor.ActivationCost is { } mana ? "/" + mana : "";
            var delay = tutor.DelayTurns > 0 ? $"/delay={tutor.DelayTurns}" : "";
            parts.Add($"tutor={Wire(tutor.Destination)}{tapped}/{Wire(tutor.Via)}{cost}{delay}");
        }

        if (profile.HasActivatedAbility)
        {
            parts.Add("activated");
        }

        if (profile.Response?.Counter is { } counter)
        {
            parts.Add(Counter(counter));
        }

        if (profile.Response is { Grants.Count: > 0 } response)
        {
            parts.Add("grants=" + string.Join('|', response.Grants.Select(grant => $"{grant.Grant}:{grant.Scope}")));
        }

        return string.Join(' ', parts);
    }

    /// <summary>A derivation result: the profile line, then
    /// "response_unprofiled(sentence)" when a sentence mentions an answer
    /// outside the response families.</summary>
    public static string Of(CardFeatureDerivation.Result result)
        => Of(result.Profile, result.UnmatchedSentence, result.ResponseUnmatchedSentence);

    /// <summary>The same line from its parts, as a stored row holds them.</summary>
    public static string Of(CardFeatureProfile profile, string? unmatchedSentence, string? responseUnmatchedSentence)
    {
        var line = Of(profile, unmatchedSentence);
        return responseUnmatchedSentence is { } sentence ? $"{line} response_unprofiled({sentence})".TrimStart() : line;
    }

    /// <summary>"counter=reach[/types=A|B][/not=A][/colors=U][/unless={N}][/loses_game]".</summary>
    private static string Counter(CounterFeature counter)
    {
        var text = "counter=" + Wire(counter.Reach);
        if (counter.Types.Count > 0)
        {
            text += "/types=" + string.Join('|', counter.Types);
        }

        if (counter.ExcludedTypes.Count > 0)
        {
            text += "/not=" + string.Join('|', counter.ExcludedTypes);
        }

        if (counter.Colors.Count > 0)
        {
            text += "/colors=" + string.Join('|', counter.Colors);
        }

        if (counter.UnlessPays is { } pays)
        {
            text += "/unless=" + pays;
        }

        return counter.LosesGame ? text + "/loses_game" : text;
    }

    /// <summary>The same line for a face the derivation left unprofiled:
    /// "unprofiled(sentence)" first, then the features the templates proved.</summary>
    public static string Of(CardFeatureProfile profile, string? unmatchedSentence)
        => unmatchedSentence is null ? Of(profile) : $"unprofiled({unmatchedSentence}) {Of(profile)}".TrimEnd();

    private static string Wire<T>(T value)
        where T : struct, Enum
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
