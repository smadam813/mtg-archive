using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Banlist;

/// <summary>
/// The banlist reference endpoints: GET /banlist/versions lists the dated
/// versions, GET /banlist/{version} serves the version a pinned
/// banlist_version names (addressed by effective_date), and
/// GET /ban-events/{id} resolves a ban-event citation. An unknown version is
/// a problem 404, never a silent latest fallback.
/// </summary>
public sealed class BanlistEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/banlist/versions", async Task<Ok<BanlistVersionsResponse>> (
            ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            var versions = await db.BanlistVersions
                .OrderBy(v => v.EffectiveDate)
                .Select(v => new BanlistVersionSummary(v.EffectiveDate, v.DatePrecision, v.AnnouncementUrl))
                .ToListAsync(cancellationToken);
            return TypedResults.Ok(new BanlistVersionsResponse(versions));
        });

        app.MapGet("/banlist/{version}", async Task<Results<Ok<BanlistVersionDetail>, ProblemHttpResult>> (
            string version, ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            if (!DateOnly.TryParseExact(version, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                return TypedResults.Problem(Problems.UnknownBanlistVersion(version));
            }

            // The ban state at a version replays everything up to it, so the
            // whole prefix loads — ~90 announcements of a few events each.
            // One query, not a split one: the replay phase can commit
            // between round trips, and a torn read would pair one point in
            // time's version row with another's events.
            var history = await db.BanlistVersions
                .Where(v => v.EffectiveDate <= date)
                .Include(v => v.Events).ThenInclude(e => e.Card)
                .AsNoTracking()
                .OrderBy(v => v.EffectiveDate)
                .ToListAsync(cancellationToken);
            var current = history.SingleOrDefault(v => v.EffectiveDate == date);
            if (current is null)
            {
                return TypedResults.Problem(Problems.UnknownBanlistVersion(version));
            }

            var dates = history.ToDictionary(v => v.Id, v => v.EffectiveDate);
            var banned = BanState.Replay(history)
                .OrderBy(e => e.Card.Name, StringComparer.Ordinal)
                .Select(e => new BannedCard(Citation.OfCard(e.Card), e.Scope,
                    EventCitation(e.Id, e.Card.Name, e.Action, e.Scope, dates[e.BanlistVersionId]), e.Category))
                .ToList();

            return TypedResults.Ok(new BanlistVersionDetail
            {
                EffectiveDate = current.EffectiveDate,
                DatePrecision = current.DatePrecision,
                AnnouncementUrl = current.AnnouncementUrl,
                Events = [.. current.Events
                    .OrderBy(e => e.Card.Name, StringComparer.Ordinal)
                    .Select(e => new BanEventDetail(e.Id, Citation.OfCard(e.Card), e.Action, e.Scope, e.Category))],
                Banned = banned,
            });
        });

        app.MapGet("/ban-events/{id:int}", async Task<Results<Ok<BanEventResponse>, ProblemHttpResult>> (
            int id, ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            // One statement joining the parent version: a replay committing
            // between two queries could cascade-delete the version after the
            // event was read, turning a 404 into a 500.
            var row = await db.BanEvents
                .Where(e => e.Id == id)
                .Join(db.BanlistVersions, e => e.BanlistVersionId, v => v.Id,
                    (e, v) => new { Event = e, e.Card, Version = v })
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            return row is null
                ? TypedResults.Problem(Problems.UnknownBanEvent(id))
                : TypedResults.Ok(new BanEventResponse(
                    row.Event.Id,
                    Citation.OfCard(row.Card),
                    row.Event.Action,
                    row.Event.Scope,
                    row.Event.Category,
                    new BanlistVersionSummary(
                        row.Version.EffectiveDate, row.Version.DatePrecision, row.Version.AnnouncementUrl)));
        });
    }

    /// <summary>The one <c>{ type: ban_event }</c> citation shape — card
    /// detail's legality rows cite the same way.</summary>
    internal static Citation EventCitation(BanReason reason, string cardName)
        => EventCitation(reason.Id, cardName, reason.Action, reason.Scope, reason.EffectiveDate);

    internal static Citation EventCitation(
        int id, string cardName, BanAction action, BanScope scope, DateOnly effectiveDate)
    {
        var verb = action == BanAction.Ban ? "banned" : "unbanned";
        var seat = scope switch
        {
            BanScope.CommanderOnly => " as commander",
            BanScope.Companion => " as companion",
            _ => "",
        };
        return new Citation(
            CitationType.BanEvent,
            id.ToString(CultureInfo.InvariantCulture),
            $"{cardName} {verb}{seat} {effectiveDate:O}");
    }
}
