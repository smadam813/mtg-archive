using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <summary>
    /// Brawl legality (ADR 0012). Legality rows of a Brawl
    /// format have no banlist version, so the key moves to the generated
    /// <c>version_key</c> (the version id, or 0). The legality boundary
    /// function gains a <c>format</c> parameter. Its version predicate
    /// admits a null version only when the caller passes a null pin, which
    /// selects the versionless Brawl rows; a Commander row always has a
    /// version, so Commander with a null pin still yields nothing. Same
    /// shape as before: STABLE and one SELECT, so Postgres inlines it.
    /// </summary>
    public partial class BrawlLegality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_legality_staging",
                table: "legality_staging");

            migrationBuilder.DropPrimaryKey(
                name: "pk_legality",
                table: "legality");

            migrationBuilder.AlterColumn<int>(
                name: "banlist_version_id",
                table: "legality_staging",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "legality_as_of",
                table: "legality_staging",
                type: "timestamp with time zone",
                nullable: true,
                comment: "The Scryfall bulk updated_at that dates a Brawl format's legality; NULL for Commander. Current state only: nobody can pin it.");

            migrationBuilder.AddColumn<string>(
                name: "scryfall_key",
                table: "legality_staging",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scryfall_uri",
                table: "legality_staging",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "banlist_version_id",
                table: "legality",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "legality_as_of",
                table: "legality",
                type: "timestamp with time zone",
                nullable: true,
                comment: "The Scryfall bulk updated_at that dates a Brawl format's legality; NULL for Commander. Current state only: nobody can pin it.");

            migrationBuilder.AddColumn<string>(
                name: "scryfall_key",
                table: "legality",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scryfall_uri",
                table: "legality",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "can_be_brawl_commander",
                table: "card",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Commander eligibility in the three Brawl formats: legendary creature, legendary planeswalker, or legendary Vehicle or Spacecraft with power and toughness (judged on the front face), or card text containing \"can be your commander\"; never a meld result. Computed at ingest.");

            migrationBuilder.AddColumn<int>(
                name: "version_key",
                table: "legality_staging",
                type: "integer",
                nullable: false,
                computedColumnSql: "COALESCE(banlist_version_id, 0)",
                stored: true,
                comment: "Key column the database generates: banlist_version_id, or 0 for a Brawl format, which has no banlist version (ADR 0012).");

            migrationBuilder.AddColumn<int>(
                name: "version_key",
                table: "legality",
                type: "integer",
                nullable: false,
                computedColumnSql: "COALESCE(banlist_version_id, 0)",
                stored: true,
                comment: "Key column the database generates: banlist_version_id, or 0 for a Brawl format, which has no banlist version (ADR 0012).");

            migrationBuilder.AddPrimaryKey(
                name: "pk_legality_staging",
                table: "legality_staging",
                columns: new[] { "card_id", "format", "version_key", "role" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_legality",
                table: "legality",
                columns: new[] { "card_id", "format", "version_key", "role" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_legality_format_version_staging",
                table: "legality_staging",
                sql: "(format = 'commander') = (banlist_version_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legality_format_version",
                table: "legality",
                sql: "(format = 'commander') = (banlist_version_id IS NOT NULL)");

            migrationBuilder.Sql(
                """
                DROP FUNCTION legal_candidates(date, text, integer, boolean);

                CREATE FUNCTION legal_candidates(
                    format text,
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
                    WHERE l.format = legal_candidates.format
                      AND (l.banlist_version_id = (SELECT v.id FROM banlist_version AS v
                                                   WHERE v.effective_date = legal_candidates.banlist_version)
                           OR (legal_candidates.banlist_version IS NULL AND l.banlist_version_id IS NULL))
                      AND l.role = legal_candidates.role
                      AND l.status = 'legal'
                      AND NOT c.stale
                      AND (legal_candidates.color_identity_mask IS NULL
                           OR c.color_identity_mask & ~legal_candidates.color_identity_mask = 0)
                      AND (NOT legal_candidates.collection_scope
                           OR EXISTS (SELECT 1 FROM collection_card AS o WHERE o.oracle_id = c.oracle_id))
                $$;

                COMMENT ON FUNCTION legal_candidates(text, date, text, integer, boolean) IS
                    'The legality boundary: every non-stale card whose materialized legality is '
                    'legal in the format (commander | brawl | competitive_brawl | standard_brawl) and '
                    'the role (commander | companion | main). Commander reads the banlist version dated '
                    'banlist_version; an unknown or null date yields nothing. A Brawl format has no '
                    'version: a null banlist_version reads its one current state, and a date yields '
                    'nothing. color_identity_mask, when given, keeps only cards whose 5-bit WUBRG mask '
                    'fits inside it (one bitwise AND). collection_scope, when true, keeps only owned '
                    'cards (a collection_card row) — caller-set, never inferred. Every index query '
                    'composes from this function and nothing else.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION legal_candidates(text, date, text, integer, boolean);

                DELETE FROM legality WHERE banlist_version_id IS NULL;
                DELETE FROM legality_staging WHERE banlist_version_id IS NULL;

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
                """);

            migrationBuilder.DropPrimaryKey(
                name: "pk_legality_staging",
                table: "legality_staging");

            migrationBuilder.DropCheckConstraint(
                name: "ck_legality_format_version_staging",
                table: "legality_staging");

            migrationBuilder.DropPrimaryKey(
                name: "pk_legality",
                table: "legality");

            migrationBuilder.DropCheckConstraint(
                name: "ck_legality_format_version",
                table: "legality");

            migrationBuilder.DropColumn(
                name: "version_key",
                table: "legality_staging");

            migrationBuilder.DropColumn(
                name: "version_key",
                table: "legality");

            migrationBuilder.DropColumn(
                name: "legality_as_of",
                table: "legality_staging");

            migrationBuilder.DropColumn(
                name: "scryfall_key",
                table: "legality_staging");

            migrationBuilder.DropColumn(
                name: "scryfall_uri",
                table: "legality_staging");

            migrationBuilder.DropColumn(
                name: "legality_as_of",
                table: "legality");

            migrationBuilder.DropColumn(
                name: "scryfall_key",
                table: "legality");

            migrationBuilder.DropColumn(
                name: "scryfall_uri",
                table: "legality");

            migrationBuilder.DropColumn(
                name: "can_be_brawl_commander",
                table: "card");

            migrationBuilder.AlterColumn<int>(
                name: "banlist_version_id",
                table: "legality_staging",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "banlist_version_id",
                table: "legality",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_legality_staging",
                table: "legality_staging",
                columns: new[] { "card_id", "format", "banlist_version_id", "role" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_legality",
                table: "legality",
                columns: new[] { "card_id", "format", "banlist_version_id", "role" });
        }
    }
}
