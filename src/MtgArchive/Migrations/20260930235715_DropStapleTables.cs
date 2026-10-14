using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class DropStapleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authoring_queue");

            migrationBuilder.DropTable(
                name: "authoring_queue_staging");

            migrationBuilder.DropTable(
                name: "staple_cut_entry");

            migrationBuilder.DropTable(
                name: "staple_cut_entry_staging");

            migrationBuilder.DropTable(
                name: "staple_definition");

            migrationBuilder.DropTable(
                name: "staple_demand");

            migrationBuilder.DropTable(
                name: "staple_cut_version");

            migrationBuilder.DropTable(
                name: "staple_cut_version_staging");

            migrationBuilder.AlterColumn<string>(
                name: "fetch_activation",
                table: "source_profile",
                type: "text",
                nullable: true,
                comment: "Lands only: untapped or tapped, the state the found land enters in, when the fetch is the closed \"{T}, [Pay N life,] Sacrifice this land: Search your library for <targets>, put it onto the battlefield [tapped], then shuffle\" template. Null for any other fetch shape: a mana cost, a trigger, a search to hand, a spell. Tier 1 and the pilot read it; XMage plays the land's own text.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "Lands only: untapped or tapped, the state the found land enters in, when the fetch is the closed \"{T}, [Pay N life,] Sacrifice this land: Search your library for <targets>, put it onto the battlefield [tapped], then shuffle\" template. Null for any other fetch shape: a mana cost, a trigger, a search to hand, a spell. The goldfish engine builds the land's one ability from it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "fetch_activation",
                table: "source_profile",
                type: "text",
                nullable: true,
                comment: "Lands only: untapped or tapped, the state the found land enters in, when the fetch is the closed \"{T}, [Pay N life,] Sacrifice this land: Search your library for <targets>, put it onto the battlefield [tapped], then shuffle\" template. Null for any other fetch shape: a mana cost, a trigger, a search to hand, a spell. The goldfish engine builds the land's one ability from it.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "Lands only: untapped or tapped, the state the found land enters in, when the fetch is the closed \"{T}, [Pay N life,] Sacrifice this land: Search your library for <targets>, put it onto the battlefield [tapped], then shuffle\" template. Null for any other fetch shape: a mana cost, a trigger, a search to hand, a spell. Tier 1 and the pilot read it; XMage plays the land's own text.");

            migrationBuilder.CreateTable(
                name: "authoring_queue",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    cut_version = table.Column<string>(type: "text", nullable: true),
                    edhrec_rank = table.Column<int>(type: "integer", nullable: true),
                    lanes = table.Column<string[]>(type: "text[]", nullable: false),
                    missing_capability = table.Column<string>(type: "text", nullable: true),
                    parked_on = table.Column<DateOnly>(type: "date", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authoring_queue", x => x.card_id);
                    table.ForeignKey(
                        name: "fk_authoring_queue_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "authoring_queue_staging",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    cut_version = table.Column<string>(type: "text", nullable: true),
                    edhrec_rank = table.Column<int>(type: "integer", nullable: true),
                    lanes = table.Column<string[]>(type: "text[]", nullable: false),
                    missing_capability = table.Column<string>(type: "text", nullable: true),
                    parked_on = table.Column<DateOnly>(type: "date", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authoring_queue_staging", x => x.card_id);
                    table.ForeignKey(
                        name: "fk_authoring_queue_card_staging",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staple_cut_version",
                columns: table => new
                {
                    version = table.Column<string>(type: "text", nullable: false, comment: "Cut version identity as echoed in versions blocks: \"yyyy-MM-dd.<ingest_run_id>\" — dated, unique per ingest run."),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cut_date = table.Column<DateOnly>(type: "date", nullable: false),
                    ingest_run_id = table.Column<int>(type: "integer", nullable: false),
                    rank_cutoff = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staple_cut_version", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "staple_cut_version_staging",
                columns: table => new
                {
                    version = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cut_date = table.Column<DateOnly>(type: "date", nullable: false),
                    ingest_run_id = table.Column<int>(type: "integer", nullable: false),
                    rank_cutoff = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staple_cut_version_staging", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "staple_definition",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    ingest_run_id = table.Column<int>(type: "integer", nullable: false),
                    oracle_sha256 = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staple_definition", x => x.card_id);
                    table.ForeignKey(
                        name: "fk_staple_definition_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staple_demand",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staple_demand", x => x.card_id);
                    table.ForeignKey(
                        name: "fk_staple_demand_card_card_id",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staple_cut_entry",
                columns: table => new
                {
                    version = table.Column<string>(type: "text", nullable: false),
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    edhrec_rank = table.Column<int>(type: "integer", nullable: true),
                    lanes = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staple_cut_entry", x => new { x.version, x.card_id });
                    table.ForeignKey(
                        name: "fk_staple_cut_entry_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staple_cut_entry_version",
                        column: x => x.version,
                        principalTable: "staple_cut_version",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staple_cut_entry_staging",
                columns: table => new
                {
                    version = table.Column<string>(type: "text", nullable: false),
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    edhrec_rank = table.Column<int>(type: "integer", nullable: true),
                    lanes = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staple_cut_entry_staging", x => new { x.version, x.card_id });
                    table.ForeignKey(
                        name: "fk_staple_cut_entry_card_staging",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staple_cut_entry_version_staging",
                        column: x => x.version,
                        principalTable: "staple_cut_version_staging",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_authoring_queue_position",
                table: "authoring_queue",
                column: "position");

            migrationBuilder.CreateIndex(
                name: "ix_authoring_queue_position_staging",
                table: "authoring_queue_staging",
                column: "position");

            migrationBuilder.CreateIndex(
                name: "ix_staple_cut_entry_card_id",
                table: "staple_cut_entry",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_staple_cut_entry_card_id_staging",
                table: "staple_cut_entry_staging",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ux_staple_definition_slug",
                table: "staple_definition",
                column: "slug",
                unique: true);
        }
    }
}
