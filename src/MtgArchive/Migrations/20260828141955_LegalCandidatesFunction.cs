using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <summary>
    /// The legality boundary's SQL function (ADR 0001;
    /// <see cref="Data.ArchiveDbContext.LegalCandidates"/>). STABLE and a
    /// single SELECT so Postgres inlines it into the calling statement; the
    /// body is a string, not <c>BEGIN ATOMIC</c>, so <c>c.*</c> follows
    /// later <c>card</c> columns instead of pinning today's column list.
    /// </summary>
    public partial class LegalCandidatesFunction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
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

                COMMENT ON FUNCTION legal_candidates(date, text, integer) IS
                    'The legality boundary: every non-stale card whose materialized legality is '
                    'legal in the role (commander | companion | main) at the banlist version dated '
                    'banlist_version; an unknown date yields nothing. color_identity_mask, when '
                    'given, keeps only cards whose 5-bit WUBRG mask fits inside it (one bitwise '
                    'AND). Every index query composes from this function and nothing else.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION legal_candidates(date, text, integer);");
        }
    }
}
