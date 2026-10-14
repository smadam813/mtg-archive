using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CorpusCardSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    oracle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    mana_cost = table.Column<string>(type: "text", nullable: true),
                    mana_value = table.Column<decimal>(type: "numeric", nullable: false),
                    type_line = table.Column<string>(type: "text", nullable: false),
                    supertypes = table.Column<string[]>(type: "text[]", nullable: false),
                    card_types = table.Column<string[]>(type: "text[]", nullable: false),
                    subtypes = table.Column<string[]>(type: "text[]", nullable: false),
                    oracle_text = table.Column<string>(type: "text", nullable: true),
                    colors = table.Column<string[]>(type: "text[]", nullable: true),
                    color_identity = table.Column<string[]>(type: "text[]", nullable: false),
                    color_identity_mask = table.Column<int>(type: "integer", nullable: false, computedColumnSql: "(CASE WHEN color_identity @> ARRAY['W'::text] THEN 16 ELSE 0 END) | (CASE WHEN color_identity @> ARRAY['U'::text] THEN 8 ELSE 0 END) | (CASE WHEN color_identity @> ARRAY['B'::text] THEN 4 ELSE 0 END) | (CASE WHEN color_identity @> ARRAY['R'::text] THEN 2 ELSE 0 END) | (CASE WHEN color_identity @> ARRAY['G'::text] THEN 1 ELSE 0 END)", stored: true, comment: "5-bit WUBRG bitmask the database generates from color_identity (W=16, U=8, B=4, R=2, G=1); a deck-identity subset test is one bitwise AND: color_identity_mask & ~deck_mask = 0."),
                    keywords = table.Column<string[]>(type: "text[]", nullable: false),
                    power = table.Column<string>(type: "text", nullable: true),
                    toughness = table.Column<string>(type: "text", nullable: true),
                    loyalty = table.Column<string>(type: "text", nullable: true),
                    produced_mana = table.Column<string[]>(type: "text[]", nullable: true),
                    game_changer = table.Column<bool>(type: "boolean", nullable: false),
                    edhrec_rank = table.Column<int>(type: "integer", nullable: true),
                    released_at = table.Column<DateOnly>(type: "date", nullable: false, comment: "First release date, stored so release-date exclusion can be added later without schema change; pinned banlist versions do not time-travel the card pool."),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false, comment: "Latest Scryfall record only, kept for re-derivation; never a historical snapshot — ingest diffs snapshots in flight."),
                    can_be_commander = table.Column<bool>(type: "boolean", nullable: false, comment: "Commander eligibility: legendary creature, or card text containing \"can be your commander\". Computed at ingest."),
                    has_partner = table.Column<bool>(type: "boolean", nullable: false, comment: "Exactly the keyword \"Partner\", not \"Partner with\"."),
                    has_partner_with = table.Column<bool>(type: "boolean", nullable: false),
                    has_friends_forever = table.Column<bool>(type: "boolean", nullable: false),
                    has_background = table.Column<bool>(type: "boolean", nullable: false, comment: "Background mechanic, either side: a Background enchantment or a commander with \"Choose a Background\"."),
                    has_doctors_companion = table.Column<bool>(type: "boolean", nullable: false),
                    copy_limit = table.Column<int>(type: "integer", nullable: true, defaultValue: 1, comment: "Per-deck allowance: 1 default (singleton); NULL = unlimited (Basic supertype or \"any number of cards named\" text); 7 for Seven Dwarves.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "card_face",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    face_index = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    mana_cost = table.Column<string>(type: "text", nullable: true),
                    mana_value = table.Column<decimal>(type: "numeric", nullable: true),
                    type_line = table.Column<string>(type: "text", nullable: false),
                    oracle_text = table.Column<string>(type: "text", nullable: true),
                    colors = table.Column<string[]>(type: "text[]", nullable: true),
                    power = table.Column<string>(type: "text", nullable: true),
                    toughness = table.Column<string>(type: "text", nullable: true),
                    loyalty = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_face", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_face_card_card_id",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_oracle_tag",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    tag = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_oracle_tag", x => new { x.card_id, x.tag });
                    table.ForeignKey(
                        name: "fk_card_oracle_tag_card_card_id",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_ruling",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<DateOnly>(type: "date", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_ruling", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_ruling_card_card_id",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_oracle_id",
                table: "card",
                column: "oracle_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_card_face_card_id_face_index",
                table: "card_face",
                columns: new[] { "card_id", "face_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_card_ruling_card_id",
                table: "card_ruling",
                column: "card_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_face");

            migrationBuilder.DropTable(
                name: "card_oracle_tag");

            migrationBuilder.DropTable(
                name: "card_ruling");

            migrationBuilder.DropTable(
                name: "card");
        }
    }
}
