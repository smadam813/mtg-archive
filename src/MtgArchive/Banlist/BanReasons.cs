using MtgArchive.Data;

namespace MtgArchive.Banlist;

/// <summary>One ban event with its version's effective date: what a
/// legality row's <see cref="Legality.Reason"/> resolves to. Init-only
/// members, not a positional constructor: EF folds a member read on an
/// object initializer inside a subquery, and cannot on a constructor.</summary>
public sealed record BanReason
{
    public required int Id { get; init; }

    public required BanAction Action { get; init; }

    public required BanScope Scope { get; init; }

    public required DateOnly EffectiveDate { get; init; }
}

/// <summary>
/// The one query that resolves a legality row's reason to its ban event.
/// Every legality projection composes it as a correlated subquery, so a
/// change to how a reason resolves lands here once. One statement per
/// caller: a replay committing between round trips can never pair a row
/// with a retired event.
/// </summary>
public static class BanReasons
{
    public static IQueryable<BanReason> Query(ArchiveDbContext db)
        => db.BanEvents.Join(db.BanlistVersions, e => e.BanlistVersionId, v => v.Id,
            (e, v) => new BanReason { Id = e.Id, Action = e.Action, Scope = e.Scope, EffectiveDate = v.EffectiveDate });
}
