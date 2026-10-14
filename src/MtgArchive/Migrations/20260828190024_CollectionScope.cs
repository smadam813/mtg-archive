using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <summary>
    /// Collection scope at the index boundary. The legality
    /// function gains a <c>collection_scope</c> parameter that hard-filters
    /// to owned cards the same way it filters legality, and
    /// <c>available_copies</c> composes scoped availability — min(copy
    /// limit, owned quantity) — into the same statement. Both are STABLE
    /// single-SELECT SQL functions: Postgres inlines the set-returning
    /// one; the scalar one (it has a FROM) runs as a cached plan per output
    /// row, and the planner defers it past ORDER BY / LIMIT, so it costs
    /// one primary-key probe per returned candidate, not per row scanned.
    /// </summary>
    public partial class CollectionScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION legal_candidates(date, text, integer);

                CREATE FUNCTION legal_candidates(
                    banlist_version date,
                    role text,
                    color_identity_mask integer DEFAULT NULL,
                    collection_scope boolean DEFAULT false)
                RETURNS SETOF card
                LANGUAGE sql
                STABLE
                AS $$
                    SELECT c.*
                    FROM card AS c
                    JOIN legality AS l ON l.card_id = c.id
                    JOIN banlist_version AS v ON v.id = l.banlist_version_id
                    WHERE v.effective_date = legal_candidates.banlist_version
                      AND l.role = legal_candidates.role
                      AND l.status = 'legal'
                      AND NOT c.stale
                      AND (legal_candidates.color_identity_mask IS NULL
                           OR c.color_identity_mask & ~legal_candidates.color_identity_mask = 0)
                      AND (NOT legal_candidates.collection_scope
                           OR EXISTS (SELECT 1 FROM collection_card AS o WHERE o.oracle_id = c.oracle_id))
                $$;

                COMMENT ON FUNCTION legal_candidates(date, text, integer, boolean) IS
                    'The legality boundary: every non-stale card whose materialized legality is '
                    'legal in the role (commander | companion | main) at the banlist version dated '
                    'banlist_version; an unknown date yields nothing. color_identity_mask, when '
                    'given, keeps only cards whose 5-bit WUBRG mask fits inside it (one bitwise '
                    'AND). collection_scope, when true, keeps only owned cards (a collection_card '
                    'row) — caller-set, never inferred. Every index query composes from this '
                    'function and nothing else.';

                CREATE FUNCTION available_copies(copy_limit integer, oracle_id uuid)
                RETURNS integer
                LANGUAGE sql
                STABLE
                AS $$
                    SELECT LEAST(available_copies.copy_limit, o.quantity)
                    FROM collection_card AS o
                    WHERE o.oracle_id = available_copies.oracle_id
                $$;

                COMMENT ON FUNCTION available_copies(integer, uuid) IS
                    'Scoped availability: min(copy limit, owned quantity). A NULL copy limit '
                    '(unlimited) yields the owned quantity — no infinite-basics assumption; an '
                    'unowned card yields NULL.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION available_copies(integer, uuid);
                DROP FUNCTION legal_candidates(date, text, integer, boolean);

                CREATE FUNCTION legal_candidates(
                    banlist_version date,
                    role text,
                    color_identity_mask integer DEFAULT NULL)
                RETURNS SETOF card
                LANGUAGE sql
                STABLE
                AS $$
                    SELECT c.*
                    FROM card AS c
                    JOIN legality AS l ON l.card_id = c.id
                    JOIN banlist_version AS v ON v.id = l.banlist_version_id
                    WHERE v.effective_date = legal_candidates.banlist_version
                      AND l.role = legal_candidates.role
                      AND l.status = 'legal'
                      AND NOT c.stale
                      AND (legal_candidates.color_identity_mask IS NULL
                           OR c.color_identity_mask & ~legal_candidates.color_identity_mask = 0)
                $$;
                """);
        }
    }
}
