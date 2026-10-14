using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// The shared staging/atomic-swap machinery for derived-state phases
/// (legality rebuild, card feature derive and source profiles).
/// Each derived table has a permanent, identically shaped companion named
/// <c>&lt;table&gt;_staging</c>, both owned by migrations. A phase truncates
/// staging, rebuilds the complete derived state there, and publishes with
/// <see cref="PublishAsync"/>: one transaction of <c>ALTER TABLE … RENAME</c>
/// exchanges, so readers flip between complete states instantly and a failed
/// build leaves the live tables untouched. Constraints and indexes (named
/// explicitly in the model, staging = live name + <c>_staging</c>) are
/// exchanged in the same transaction, so live object names stay canonical —
/// exactly what the EF model snapshot records — after any number of swaps.
/// </summary>
public static class DerivedStateSwap
{
    public const string StagingSuffix = "_staging";

    /// <summary>
    /// One live/staging table pair: the live table name plus every
    /// explicitly named constraint and index on it. The staging twin's
    /// names must be these plus <see cref="StagingSuffix"/>.
    /// </summary>
    public sealed record SwappedTable(string Table, string[] Constraints, string[] Indexes);

    /// <summary>Empties the staging tables (one statement, so FKs between them don't order it).</summary>
    public static Task TruncateStagingAsync(
        ArchiveDbContext db, IReadOnlyList<SwappedTable> tables, CancellationToken cancellationToken)
    {
        var names = string.Join(", ", tables.Select(t => Quote(t.Table + StagingSuffix)));
        // EF1002: every identifier is a compile-time spec constant (see Quote).
#pragma warning disable EF1002
        return db.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {names}", cancellationToken);
#pragma warning restore EF1002
    }

    /// <summary>
    /// Atomically exchanges every live table with its populated staging twin.
    /// After commit the old live rows sit in staging until the next build
    /// truncates them.
    /// </summary>
    public static async Task PublishAsync(
        ArchiveDbContext db, IReadOnlyList<SwappedTable> tables, CancellationToken cancellationToken)
    {
        // One round trip: the renames ship as a single multi-statement
        // command inside the transaction, so the count of swapped objects
        // never multiplies network hops.
        var script = string.Join(";\n", tables.SelectMany(SwapStatements));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // EF1002: every identifier is a compile-time spec constant (see Quote).
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(script, cancellationToken);
#pragma warning restore EF1002
        await transaction.CommitAsync(cancellationToken);
    }

    private static IEnumerable<string> SwapStatements(SwappedTable table)
    {
        foreach (var statement in Exchange(table.Table,
            (from, to) => $"ALTER TABLE {Quote(from)} RENAME TO {Quote(to)}"))
        {
            yield return statement;
        }

        // Table renames leave constraint and index names behind (they attach
        // to the table's identity, not its name), so each gets the same
        // three-step exchange. Renaming a PK or unique constraint renames its
        // backing index with it, which is what frees the schema-global index
        // name for the incoming table.
        foreach (var constraint in table.Constraints)
        {
            foreach (var statement in Exchange(constraint, (from, to) =>
                $"ALTER TABLE {Quote(OwnerOf(table, from))} RENAME CONSTRAINT {Quote(from)} TO {Quote(to)}"))
            {
                yield return statement;
            }
        }

        foreach (var index in table.Indexes)
        {
            foreach (var statement in Exchange(index,
                (from, to) => $"ALTER INDEX {Quote(from)} RENAME TO {Quote(to)}"))
            {
                yield return statement;
            }
        }
    }

    /// <summary>
    /// The three renames that exchange canonical and staging names through a
    /// <c>__swap</c> placeholder: canonical → placeholder, staging →
    /// canonical, placeholder → staging.
    /// </summary>
    private static IEnumerable<string> Exchange(string name, Func<string, string, string> rename)
    {
        yield return rename(name, name + "__swap");
        yield return rename(name + StagingSuffix, name);
        yield return rename(name + "__swap", name + StagingSuffix);
    }

    /// <summary>
    /// The table currently holding the constraint at each exchange step:
    /// canonically named constraints ride the outgoing table (renamed to
    /// staging first), staging-named ones the incoming (now canonical) table.
    /// </summary>
    private static string OwnerOf(SwappedTable table, string constraint)
        => constraint.EndsWith(StagingSuffix, StringComparison.Ordinal)
            ? table.Table
            : table.Table + StagingSuffix;

    // Identifiers come from compile-time specs, never input; quoting guards
    // against future reserved-word collisions only.
    private static string Quote(string identifier) => $"\"{identifier}\"";
}
