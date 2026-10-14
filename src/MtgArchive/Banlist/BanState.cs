using MtgArchive.Data;

namespace MtgArchive.Banlist;

/// <summary>
/// The one definition of ban state (GLOSSARY.md "Banlist version"): order the
/// versions by effective date, take each card's latest event, and the card
/// is banned iff that event is a ban — with that event's scope. An event
/// states the card's new state, so an unban lifts whatever ban preceded it.
/// Dates never tie: effective_date is unique. The banlist endpoints and the
/// legality rebuild phase compute the same rule; keep every call site on
/// this method.
/// </summary>
public static class BanState
{
    /// <summary>The standing bans after replaying <paramref name="history"/>.</summary>
    public static IReadOnlyList<BanEvent> Replay(IEnumerable<BanlistVersion> history)
        => [.. history
            .OrderBy(v => v.EffectiveDate)
            .SelectMany(v => v.Events)
            .GroupBy(e => e.CardId)
            .Select(g => g.Last())
            .Where(e => e.Action == BanAction.Ban)];
}
