using System.Globalization;
using System.Text.Json;
using MtgArchive.Data;

namespace MtgArchive.Banlist;

/// <summary>
/// Parses and validates the curated banlist events file
/// (<c>data/banlist-events.json</c>, ADR 0004) — the source of truth for
/// Commander banlist history that the replay phase rebuilds the derived
/// tables from. The file is a JSON array of versions, one per dated
/// announcement, each exactly
/// <c>{ effective_date, date_precision, announcement_url, events }</c> with
/// events exactly <c>{ card, action, scope }</c>. Every problem lands in the
/// caller's error list; a file with errors never reaches the database.
/// </summary>
public static class BanlistEventsFile
{
    /// <summary>The compose stack's read-only /data mount — the fallback when
    /// Banlist:EventsPath is not configured.</summary>
    public const string DefaultPath = "/data/banlist-events.json";

    public static IReadOnlyList<BanlistFileVersion> Read(string path, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"banlist-events: no file at '{path}'");
            return [];
        }

        return Parse(File.ReadAllText(path), errors);
    }

    public static IReadOnlyList<BanlistFileVersion> Parse(string json, List<string> errors)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errors.Add($"banlist-events: invalid JSON ({ex.Message})");
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add("banlist-events: the file must be a JSON array of versions");
                return [];
            }

            var versions = new List<BanlistFileVersion>();
            // Dates are collected apart from the parsed versions so a
            // duplicate date is flagged even when its entry also has an
            // unrelated defect that kept it out of the parsed list.
            var dates = new List<DateOnly>();
            var index = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var (version, effectiveDate) = ParseVersion(element, $"banlist-events versions[{index}]", errors);
                if (version is not null)
                {
                    versions.Add(version);
                }

                if (effectiveDate is { } date)
                {
                    dates.Add(date);
                }

                index++;
            }

            foreach (var duplicate in dates.GroupBy(d => d).Where(g => g.Count() > 1))
            {
                errors.Add(
                    $"banlist-events: {duplicate.Count()} versions share effective_date "
                    + $"{duplicate.Key:O} — one version per dated announcement");
            }

            return versions;
        }
    }

    private static (BanlistFileVersion? Version, DateOnly? EffectiveDate) ParseVersion(
        JsonElement element, string where, List<string> errors)
    {
        var before = errors.Count;
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{where}: a version must be an object");
            return (null, null);
        }

        RejectUnknownMembers(element, where, ["effective_date", "date_precision", "announcement_url", "events"], errors);

        var precision = ParseDatePrecision(element, where, errors);
        var effectiveDate = ParseEffectiveDate(element, precision, where, errors);
        var announcementUrl = ParseAnnouncementUrl(element, where, errors);
        var events = ParseEvents(element, where, errors);

        // errors.Count still gates (an unknown member alone discards the
        // version), but construction checks the locals themselves.
        var version = precision is { } parsedPrecision
            && effectiveDate is { } parsedDate
            && announcementUrl is { } parsedUrl
            && events is { } parsedEvents
            && errors.Count == before
                ? new BanlistFileVersion(parsedDate, parsedPrecision, parsedUrl, parsedEvents)
                : null;
        return (version, effectiveDate);
    }

    internal static DatePrecision? ParseDatePrecision(JsonElement element, string where, List<string> errors)
        => RequiredString(element, "date_precision", where, errors) switch
        {
            null => null,
            "day" => DatePrecision.Day,
            "month" => DatePrecision.Month,
            var other => Unknown<DatePrecision>(where, "date_precision", other, "day|month", errors),
        };

    /// <summary>
    /// Day-precision dates are full ISO dates; the 2002–2009 era announced
    /// by month only, so month precision takes <c>yyyy-MM</c>, normalized to
    /// the first of the month. A month-precision date naming any other day
    /// claims precision the announcement doesn't have, so it is an error —
    /// never silently normalized.
    /// </summary>
    internal static DateOnly? ParseEffectiveDate(
        JsonElement element, DatePrecision? precision, string where, List<string> errors)
    {
        var text = RequiredString(element, "effective_date", where, errors);
        if (text is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            if (precision is DatePrecision.Month && date.Day != 1)
            {
                errors.Add($"{where}: effective_date '{text}' names a day but date_precision is month — write yyyy-MM");
                return null;
            }

            return date;
        }

        if ((precision is null or DatePrecision.Month)
            && DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            return month;
        }

        errors.Add(precision is DatePrecision.Month
            ? $"{where}: effective_date '{text}' is not yyyy-MM-dd or yyyy-MM"
            : $"{where}: effective_date '{text}' is not yyyy-MM-dd");
        return null;
    }

    internal static string? ParseAnnouncementUrl(JsonElement element, string where, List<string> errors)
    {
        var url = RequiredString(element, "announcement_url", where, errors);
        if (url is null)
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{where}: announcement_url '{url}' is not an absolute http(s) URL");
            return null;
        }

        return url;
    }

    private static IReadOnlyList<BanlistFileEvent>? ParseEvents(JsonElement element, string where, List<string> errors)
    {
        if (!element.TryGetProperty("events", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{where}: missing or invalid events");
            return null;
        }

        var before = errors.Count;
        var events = new List<BanlistFileEvent>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (ParseEvent(item, $"{where} events[{index}]", errors) is { } banEvent)
            {
                events.Add(banEvent);
            }

            index++;
        }

        foreach (var duplicate in events.GroupBy(e => e.Card, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"{where}: '{duplicate.Key}' appears {duplicate.Count()} times — one event per card per announcement");
        }

        return errors.Count == before ? events : null;
    }

    private static BanlistFileEvent? ParseEvent(JsonElement element, string where, List<string> errors)
    {
        var before = errors.Count;
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{where}: an event must be an object");
            return null;
        }

        RejectUnknownMembers(element, where, ["card", "action", "scope"], errors);

        var card = RequiredString(element, "card", where, errors);
        var action = RequiredString(element, "action", where, errors) switch
        {
            null => (BanAction?)null,
            "ban" => BanAction.Ban,
            "unban" => BanAction.Unban,
            var other => Unknown<BanAction>(where, "action", other, "ban|unban", errors),
        };
        var scope = RequiredString(element, "scope", where, errors) switch
        {
            null => (BanScope?)null,
            "all" => BanScope.All,
            "commander_only" => BanScope.CommanderOnly,
            "companion" => BanScope.Companion,
            var other => Unknown<BanScope>(where, "scope", other, "all|commander_only|companion", errors),
        };

        return card is { } parsedCard && action is { } parsedAction && scope is { } parsedScope
            && errors.Count == before
                ? new BanlistFileEvent(parsedCard, parsedAction, parsedScope)
                : null;
    }

    internal static void RejectUnknownMembers(
        JsonElement element, string where, string[] known, List<string> errors)
    {
        foreach (var member in element.EnumerateObject())
        {
            if (!known.Contains(member.Name, StringComparer.Ordinal))
            {
                errors.Add($"{where}: unknown member '{member.Name}'");
            }
        }
    }

    internal static string? RequiredString(JsonElement element, string name, string where, List<string> errors)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        errors.Add($"{where}: missing or invalid {name}");
        return null;
    }

    internal static T? Unknown<T>(string where, string member, string value, string allowed, List<string> errors)
        where T : struct
    {
        errors.Add($"{where}: unknown {member} '{value}' (expected {allowed})");
        return null;
    }
}

/// <summary>One version from the events file — a dated announcement and its changes.</summary>
public sealed record BanlistFileVersion(
    DateOnly EffectiveDate,
    DatePrecision DatePrecision,
    string AnnouncementUrl,
    IReadOnlyList<BanlistFileEvent> Events);

/// <summary>One change from the events file; the card is an exact Oracle name.</summary>
public sealed record BanlistFileEvent(string Card, BanAction Action, BanScope Scope);
