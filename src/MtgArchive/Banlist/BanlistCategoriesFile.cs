using System.Text.Json;
using MtgArchive.Data;

namespace MtgArchive.Banlist;

/// <summary>
/// Parses and validates the curated banlist categories file
/// (<c>data/banlist-categories.json</c>, ADR 0010) — the category lines the
/// official list prints instead of card names (ante cards, Conspiracy
/// cards, the cards Wizards removed in 2020). The file is a JSON array of
/// categories, each exactly
/// <c>{ category, list_wording, effective_date, date_precision, announcement_url, members }</c>,
/// where <c>members</c> holds exactly one rule of the closed vocabulary:
/// <c>card_type</c> (a card type the card carries), <c>oracle_word</c> (a
/// whole word in the Oracle text of the card or any face, case-insensitive),
/// or <c>cards</c> (exact Oracle names). A category is a ban in every seat
/// from its effective date; the replay phase expands it into ban events.
/// Every problem lands in the caller's error list (the banlist-events
/// pattern); a file with errors never reaches the database.
/// </summary>
public static class BanlistCategoriesFile
{
    /// <summary>The compose stack's read-only /data mount — the fallback when
    /// Banlist:CategoriesPath is not configured.</summary>
    public const string DefaultPath = "/data/banlist-categories.json";

    public static IReadOnlyList<BanlistFileCategory> Read(string path, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"banlist-categories: no file at '{path}'");
            return [];
        }

        return Parse(File.ReadAllText(path), errors);
    }

    public static IReadOnlyList<BanlistFileCategory> Parse(string json, List<string> errors)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errors.Add($"banlist-categories: invalid JSON ({ex.Message})");
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add("banlist-categories: the file must be a JSON array of categories");
                return [];
            }

            var categories = new List<BanlistFileCategory>();
            var keys = new List<string>();
            var index = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var (category, key) = ParseCategory(element, $"banlist-categories categories[{index}]", errors);
                if (category is not null)
                {
                    categories.Add(category);
                }

                if (key is not null)
                {
                    keys.Add(key);
                }

                index++;
            }

            foreach (var duplicate in keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                errors.Add($"banlist-categories: {duplicate.Count()} categories share the key '{duplicate.Key}'");
            }

            return categories;
        }
    }

    private static (BanlistFileCategory? Category, string? Key) ParseCategory(
        JsonElement element, string where, List<string> errors)
    {
        var before = errors.Count;
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{where}: a category must be an object");
            return (null, null);
        }

        BanlistEventsFile.RejectUnknownMembers(element, where,
            ["category", "list_wording", "effective_date", "date_precision", "announcement_url", "members"], errors);

        var key = BanlistEventsFile.RequiredString(element, "category", where, errors);
        var wording = BanlistEventsFile.RequiredString(element, "list_wording", where, errors);
        var precision = BanlistEventsFile.ParseDatePrecision(element, where, errors);
        var effectiveDate = BanlistEventsFile.ParseEffectiveDate(element, precision, where, errors);
        var announcementUrl = BanlistEventsFile.ParseAnnouncementUrl(element, where, errors);
        var members = ParseMembers(element, where, errors);

        var category = key is { } parsedKey
            && wording is { } parsedWording
            && precision is { } parsedPrecision
            && effectiveDate is { } parsedDate
            && announcementUrl is { } parsedUrl
            && members is { } parsedMembers
            && errors.Count == before
                ? new BanlistFileCategory(parsedKey, parsedWording, parsedDate, parsedPrecision, parsedUrl, parsedMembers)
                : null;
        return (category, key);
    }

    /// <summary>
    /// Exactly one member rule. The vocabulary is closed on purpose: an
    /// unknown rule is an error, never a category that silently matches
    /// nothing.
    /// </summary>
    private static CategoryMembers? ParseMembers(JsonElement element, string where, List<string> errors)
    {
        if (!element.TryGetProperty("members", out var value) || value.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{where}: missing or invalid members");
            return null;
        }

        var rules = value.EnumerateObject().ToList();
        if (rules.Count != 1)
        {
            errors.Add($"{where}: members must hold exactly one rule (card_type|oracle_word|cards)");
            return null;
        }

        var rule = rules[0];
        var at = $"{where} members";
        switch (rule.Name)
        {
            case "card_type":
                return BanlistEventsFile.RequiredString(value, "card_type", at, errors) is { } type
                    ? new CategoryMembers.CardType(type)
                    : null;
            case "oracle_word":
                return BanlistEventsFile.RequiredString(value, "oracle_word", at, errors) is { } word
                    ? word.All(char.IsLetter)
                        ? new CategoryMembers.OracleWord(word)
                        : Invalid(at, $"oracle_word '{word}' must be letters only", errors)
                    : null;
            case "cards":
                return ParseCards(rule.Value, at, errors);
            default:
                errors.Add($"{at}: unknown rule '{rule.Name}' (expected card_type|oracle_word|cards)");
                return null;
        }
    }

    private static CategoryMembers? ParseCards(JsonElement value, string where, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            return Invalid(where, "cards must be a non-empty array of exact Oracle names", errors);
        }

        var names = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } name)
            {
                return Invalid(where, "cards must be a non-empty array of exact Oracle names", errors);
            }

            names.Add(name);
        }

        var duplicates = names.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
        foreach (var duplicate in duplicates)
        {
            errors.Add($"{where}: '{duplicate.Key}' appears {duplicate.Count()} times");
        }

        return duplicates.Count == 0 ? new CategoryMembers.Cards(names) : null;
    }

    private static CategoryMembers? Invalid(string where, string message, List<string> errors)
    {
        errors.Add($"{where}: {message}");
        return null;
    }
}

/// <summary>One category from the categories file: the list's own wording,
/// the dated announcement that put it in force, and the member rule.</summary>
public sealed record BanlistFileCategory(
    string Key,
    string ListWording,
    DateOnly EffectiveDate,
    DatePrecision DatePrecision,
    string AnnouncementUrl,
    CategoryMembers Members);

/// <summary>The closed vocabulary of category member rules.</summary>
public abstract record CategoryMembers
{
    private CategoryMembers()
    {
    }

    /// <summary>Cards whose card types include <paramref name="Type"/>.</summary>
    public sealed record CardType(string Type) : CategoryMembers;

    /// <summary>Cards whose Oracle text, or any face's, holds
    /// <paramref name="Word"/> as a whole word, case-insensitive.</summary>
    public sealed record OracleWord(string Word) : CategoryMembers;

    /// <summary>Cards named one by one, by exact Oracle name.</summary>
    public sealed record Cards(IReadOnlyList<string> Names) : CategoryMembers
    {
        public bool Equals(Cards? other) => other is not null && Names.SequenceEqual(other.Names, StringComparer.Ordinal);

        public override int GetHashCode() => Names.Count;
    }
}
