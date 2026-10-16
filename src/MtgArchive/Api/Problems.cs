using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MtgArchive.CollectionImport;
using MtgArchive.Simulation;

namespace MtgArchive.Api;

/// <summary>
/// RFC 9457 error convention: every error is <c>application/problem+json</c>,
/// carrying domain extension members (snake_case keys, e.g.
/// <c>unresolved_names</c>) where they help the caller. Only "the request
/// can't be answered" is an error — quarantine rows and drift are report
/// content inside 2xx responses.
/// </summary>
public static class Problems
{
    public static ProblemDetails UnknownVersion(string parameter, string value) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-version",
        Title = "Unknown version",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"Unknown {parameter} '{value}'. Omit the parameter to use the latest version.",
    }
        .WithExtension("parameter", parameter)
        .WithExtension("value", value);

    /// <summary>The request names a format the service does not serve. A
    /// missing format means Commander; a named one is never replaced.</summary>
    public static ProblemDetails UnknownFormat(string value, IReadOnlyList<string> known) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-format",
        Title = "Unknown format",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"Unknown format '{value}'. The known formats are {string.Join(", ", known)}. "
            + "Omit the field to use commander.",
    }
        .WithExtension("parameter", "format")
        .WithExtension("value", value)
        .WithExtension("known", known);

    /// <summary>The request pins a version axis the format does not have:
    /// a Brawl format has no banlist version (ADR 0012).</summary>
    public static ProblemDetails UnversionedFormat(string parameter, string format) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unversioned-format",
        Title = "Format has no such version",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"The {format} format has no {parameter}: its legality is one current state. "
            + $"Omit {parameter}, or use the commander format.",
    }
        .WithExtension("parameter", parameter)
        .WithExtension("format", format);

    /// <summary>The format is known, but the route does not serve it yet:
    /// Standard has card legality only, so no deck route takes it.</summary>
    public static ProblemDetails UnservedFormat(string format, IReadOnlyList<string> served) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unserved-format",
        Title = "Format not served by this route",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"This route does not serve the {format} format yet: it has card legality only. "
            + $"The served formats are {string.Join(", ", served)}.",
    }
        .WithExtension("parameter", "format")
        .WithExtension("value", format)
        .WithExtension("served", served);

    /// <summary>The route serves Commander only: brackets, the bracket spec,
    /// and the Game Changers list have no Brawl counterpart (ADR 0012).</summary>
    public static ProblemDetails CommanderOnly(string format) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:commander-only",
        Title = "Commander only",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"Brackets apply to Commander only. The {format} format has no power target: "
            + "Arena publishes no bracket system for Brawl. Omit format, or use commander.",
    }
        .WithExtension("parameter", "format")
        .WithExtension("value", format);

    public static ProblemDetails UnknownCard(Guid oracleId) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-card",
        Title = "Unknown card",
        Status = StatusCodes.Status404NotFound,
        Detail = $"No card with oracle_id '{oracleId}'.",
    }
        .WithExtension("oracle_id", oracleId);

    public static ProblemDetails UnknownCardName(string name) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-card-name",
        Title = "Unknown card name",
        Status = StatusCodes.Status404NotFound,
        Detail = $"No card named '{name}'. The lookup is exact-name only.",
    }
        .WithExtension("name", name);

    /// <summary>
    /// More than one card at the same staleness shares the requested name.
    /// Nothing enforces name uniqueness, so this is corpus corruption to
    /// repair — a 500, not a caller error.
    /// </summary>
    public static ProblemDetails AmbiguousCardName(string name, IEnumerable<Guid> oracleIds) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:ambiguous-card-name",
        Title = "Ambiguous card name",
        Status = StatusCodes.Status500InternalServerError,
        Detail = $"More than one card is named '{name}' — the corpus needs repair. Fetch by oracle_id instead.",
    }
        .WithExtension("name", name)
        .WithExtension("oracle_ids", oracleIds.ToArray());

    public static ProblemDetails UnknownRule(string number) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-rule",
        Title = "Unknown rule",
        Status = StatusCodes.Status404NotFound,
        Detail = $"No CR rule or glossary entry '{number}'. Rules resolve at chunk granularity "
            + "(e.g. 601.2 or 601.2b); glossary terms are exact.",
    }
        .WithExtension("number", number);

    public static ProblemDetails MissingParameter(string parameter) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:missing-parameter",
        Title = "Missing parameter",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"The '{parameter}' parameter is required.",
    }
        .WithExtension("parameter", parameter);

    public static ProblemDetails ParameterBelowOne(string parameter, int value) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:parameter-below-one",
        Title = "Parameter below one",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"'{parameter}' must be at least 1; got {value}. Omit it for the default.",
    }
        .WithExtension("parameter", parameter)
        .WithExtension("value", value);

    public static ProblemDetails ParameterAboveMaximum(string parameter, long value, long maximum) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:parameter-above-maximum",
        Title = "Parameter above maximum",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"'{parameter}' must be at most {maximum}; got {value}.",
    }
        .WithExtension("parameter", parameter)
        .WithExtension("value", value)
        .WithExtension("maximum", maximum);

    /// <summary>
    /// The goal predicate does not parse, names a card outside the
    /// decklist, or resolves after the turn horizon. The engine cannot
    /// answer any of these: a goal the deck cannot meet reports a floor of
    /// 0% and hides the mistake. <paramref name="vocabulary"/> is the
    /// closed predicate vocabulary the detail names.
    /// </summary>
    public static ProblemDetails InvalidGoal(IReadOnlyList<string> errors, IReadOnlyList<string> vocabulary) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:invalid-goal",
        Title = "Invalid goal",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"The goal is not a valid goal predicate. The vocabulary is {string.Join(", ", vocabulary)}.",
    }
        .WithExtension("errors", errors);

    /// <summary>
    /// The disruption profile does not parse or names a kind outside the
    /// closed vocabulary. An unknown kind is a request error, never a
    /// warning (ADR 0005).
    /// </summary>
    public static ProblemDetails InvalidDisruptionProfile(IReadOnlyList<string> errors, IReadOnlyList<string> vocabulary) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:invalid-disruption-profile",
        Title = "Invalid disruption profile",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"The disruption profile is not a valid schedule of events. The vocabulary is {string.Join(", ", vocabulary)}.",
    }
        .WithExtension("errors", errors);

    /// <summary>
    /// The disruption mode names no mode of the closed list. The
    /// <c>allowed</c> member lists the modes (ADR 0013).
    /// </summary>
    public static ProblemDetails UnknownDisruptionMode(string value, IReadOnlyList<string> allowed) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:invalid-disruption-mode",
        Title = "Invalid disruption mode",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"Unknown disruption_mode '{value}'. The modes are {string.Join(", ", allowed)}.",
    }
        .WithExtension("value", value)
        .WithExtension("allowed", allowed);

    /// <summary>
    /// The request names a disruption mode but no disruption profile. A
    /// mode resolves the profile's events, so it means nothing alone
    /// (ADR 0013).
    /// </summary>
    public static ProblemDetails DisruptionModeWithoutProfile(string value) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:invalid-disruption-mode",
        Title = "Invalid disruption mode",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"disruption_mode '{value}' needs a disruption_profile. Send a profile, or omit the mode.",
    }
        .WithExtension("value", value);

    /// <summary>The play policy is not in the list. The
    /// <c>allowed</c> member lists the policies (ADR 0014).</summary>
    public static ProblemDetails UnknownPlayPolicy(string value, IReadOnlyList<string> allowed) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:invalid-play-policy",
        Title = "Invalid play policy",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"Unknown play_policy '{value}'. The policies are {string.Join(", ", allowed)}.",
    }
        .WithExtension("value", value)
        .WithExtension("allowed", allowed);

    /// <summary>The hold policy holds mana for the interactive disruption
    /// mode, so it means nothing in any other run (ADR 0014).</summary>
    public static ProblemDetails PlayPolicyNeedsInteractive(string value) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:invalid-play-policy",
        Title = "Invalid play policy",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"play_policy '{value}' needs disruption_mode interactive. Send the mode and a disruption_profile, or omit the policy.",
    }
        .WithExtension("value", value);

    /// <summary>The configured engine cannot be reached, refused its
    /// handshake, or changed during the request. No floor comes back: the
    /// request played no game the service can count.</summary>
    public static ProblemDetails EngineUnavailable(string engine, string detail) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:engine-unavailable",
        Title = "Engine unavailable",
        Status = StatusCodes.Status503ServiceUnavailable,
        Detail = $"The simulation engine {engine} is unavailable: {detail}",
    }
        .WithExtension("engine", engine);

    /// <summary>More than 1% of the games ended in an engine error (spec
    /// §4.5). A floor over them would measure the engine's failures, not
    /// the deck.</summary>
    public static ProblemDetails EngineErrors(string engine, string version, int errors, int games) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:engine-errors",
        Title = "Too many engine errors",
        Status = StatusCodes.Status502BadGateway,
        Detail = $"{errors} of {games} games on {engine} {version} ended in an engine error. The limit is 1%.",
    }
        .WithExtension("engine", engine)
        .WithExtension("version", version)
        .WithExtension("engine_errors", errors)
        .WithExtension("games", games);

    /// <summary>The engine does not implement a commander, so it cannot
    /// play the deck at all.</summary>
    public static ProblemDetails EngineCannotRun(string engine, string version, IReadOnlyList<string> cards) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:engine-cannot-run",
        Title = "Engine cannot run the deck",
        Status = StatusCodes.Status422UnprocessableEntity,
        Detail = $"{engine} {version} does not implement the commander {string.Join(", ", cards)}.",
    }
        .WithExtension("engine", engine)
        .WithExtension("cards", cards);

    /// <summary>
    /// The path addressed a banlist version that was never replayed — a
    /// malformed date included: it addresses nothing, never a silent latest
    /// fallback.
    /// </summary>
    public static ProblemDetails UnknownBanlistVersion(string version) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-banlist-version",
        Title = "Unknown banlist version",
        Status = StatusCodes.Status404NotFound,
        Detail = $"No banlist version '{version}'. Versions are addressed by effective_date "
            + "(yyyy-MM-dd) — GET /banlist/versions lists them.",
    }
        .WithExtension("version", version);

    public static ProblemDetails UnknownBanEvent(int id) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-ban-event",
        Title = "Unknown ban event",
        Status = StatusCodes.Status404NotFound,
        Detail = $"No ban event '{id}'.",
    }
        .WithExtension("id", id);

    /// <summary>
    /// The uploaded collection file is structurally corrupt (bad header,
    /// ragged row, non-numeric quantity) and cannot be parsed at all. Rows
    /// that merely fail to resolve are quarantine report content, never this.
    /// </summary>
    public static ProblemDetails InvalidImportContent(string detail) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:invalid-collection-import",
        Title = "Invalid collection import",
        Status = StatusCodes.Status400BadRequest,
        Detail = detail,
    };

    /// <summary>
    /// Not one row resolved to a card (empty content, a header-only CSV, or
    /// every row quarantined). That is far likelier a wrong file or an
    /// un-ingested corpus than an intent to empty the collection, so the
    /// replace is refused and the quarantine rides along here instead.
    /// </summary>
    public static ProblemDetails EmptyCollectionImport(IReadOnlyList<QuarantineEntry> quarantine) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:empty-collection-import",
        Title = "Empty collection import",
        Status = StatusCodes.Status400BadRequest,
        Detail = "No row resolved to a card, so the import would empty the collection. "
            + "The stored collection is unchanged.",
    }
        .WithExtension("quarantine", quarantine);

    public static ProblemDetails InvalidColorIdentity(IReadOnlyList<string> unknown) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:invalid-color-identity",
        Title = "Invalid color identity",
        Status = StatusCodes.Status400BadRequest,
        Detail = "color_identity takes the letters W, U, B, R, G only. "
            + $"Unknown: {string.Join(", ", unknown.Select(u => $"'{u}'"))}.",
    }
        .WithExtension("unknown", unknown);

    /// <summary>
    /// A decklist or category card did not resolve to a known card. Tier 1
    /// never drops a card: a missing land skews every table. The whole
    /// request is refused and <c>unresolved_names</c> lists every failure.
    /// </summary>
    public static ProblemDetails UnresolvedDecklist(IReadOnlyList<UnresolvedCard> unresolved) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unresolved-decklist",
        Title = "Unresolved decklist",
        Status = StatusCodes.Status400BadRequest,
        Detail = "Some cards did not resolve to a known card. Names are exact Oracle names "
            + "(a bare front-face name is accepted, and an A- name falls back to the original card); "
            + "oracle_ids must exist in the corpus.",
    }
        .WithExtension("unresolved_names", unresolved.Select(u => u.ToWire()).ToArray());

    public static ProblemDetails InvalidDecklist(string detail) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:invalid-decklist",
        Title = "Invalid decklist",
        Status = StatusCodes.Status400BadRequest,
        Detail = detail,
    };

    /// <summary>
    /// A version axis has nothing to default to: ingest has loaded no
    /// bracket spec or Game Changers snapshot yet, so the service cannot
    /// answer a classification. It never falls back to a hardcoded spec.
    /// </summary>
    public static ProblemDetails NothingIngested(string parameter) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:nothing-ingested",
        Title = "Nothing ingested",
        Status = StatusCodes.Status409Conflict,
        Detail = $"No version exists for '{parameter}' yet. POST /ingest loads the data files first.",
    }
        .WithExtension("parameter", parameter);

    /// <summary>The pinned spec revision defines no such bracket.</summary>
    public static ProblemDetails UnknownBracket(string parameter, int value, IReadOnlyList<int> known) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-bracket",
        Title = "Unknown bracket",
        Status = StatusCodes.Status400BadRequest,
        Detail = $"'{parameter}' is {value}; the bracket spec defines brackets {string.Join(", ", known)}.",
    }
        .WithExtension("parameter", parameter)
        .WithExtension("value", value)
        .WithExtension("known", known);

    /// <summary>The path names a bracket spec version or Game Changers
    /// snapshot that ingest never loaded, a malformed date included.</summary>
    public static ProblemDetails UnknownSnapshot(string kind, string date) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-snapshot",
        Title = "Unknown snapshot",
        Status = StatusCodes.Status404NotFound,
        Detail = $"No {kind} '{date}'. The date (yyyy-MM-dd) addresses a snapshot — GET /{kind}/versions lists them.",
    }
        .WithExtension("kind", kind)
        .WithExtension("date", date);

    public static ProblemDetails UnknownIngestRun(string id) => new ProblemDetails
    {
        Type = "urn:mtg-archive:problem:unknown-ingest-run",
        Title = "Unknown ingest run",
        Status = StatusCodes.Status404NotFound,
        Detail = $"No ingest run '{id}'. POST /ingest to start one.",
    }
        .WithExtension("run_id", id);

    /// <summary>
    /// The one way to add a domain extension member. Normalizes the key to
    /// snake_case: the framework's ProblemDetails serializer writes extension
    /// keys verbatim, bypassing <see cref="WireJson"/>'s dictionary policy.
    /// </summary>
    public static ProblemDetails WithExtension(this ProblemDetails problem, string name, object? value)
    {
        problem.Extensions[JsonNamingPolicy.SnakeCaseLower.ConvertName(name)] = value;
        return problem;
    }
}

/// <summary>
/// Thrown by shared request resolution (version pins, color identity) when
/// the request can't be answered, so endpoints never branch on it:
/// <see cref="ProblemExceptionHandler"/> writes the carried problem.
/// </summary>
public sealed class ProblemException(ProblemDetails problem) : Exception(problem.Detail)
{
    public ProblemDetails Problem { get; } = problem;
}

public sealed class ProblemExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ProblemException { Problem: var problem })
        {
            return false;
        }

        // Every ProblemException is a request resolution couldn't answer, so
        // a problem built without a status is a 400, never a crash here.
        httpContext.Response.StatusCode = problem.Status ??= StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
