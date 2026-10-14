using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class BracketSpecData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "barometer_card",
                columns: table => new
                {
                    barometer = table.Column<string>(type: "text", nullable: false),
                    card_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barometer_card", x => new { x.barometer, x.card_id });
                    table.ForeignKey(
                        name: "fk_barometer_card_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bracket_spec_version",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    ingest_run_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bracket_spec_version", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "game_changers_snapshot",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    ingest_run_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_changers_snapshot", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "game_changers_snapshot_card",
                columns: table => new
                {
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    card_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_changers_snapshot_card", x => new { x.snapshot_date, x.card_id });
                    table.ForeignKey(
                        name: "fk_game_changers_snapshot_card_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_game_changers_snapshot_card_snapshot",
                        column: x => x.snapshot_date,
                        principalTable: "game_changers_snapshot",
                        principalColumn: "date",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_barometer_card_card_id",
                table: "barometer_card",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_changers_snapshot_card_card_id",
                table: "game_changers_snapshot_card",
                column: "card_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "barometer_card");

            migrationBuilder.DropTable(
                name: "bracket_spec_version");

            migrationBuilder.DropTable(
                name: "game_changers_snapshot_card");

            migrationBuilder.DropTable(
                name: "game_changers_snapshot");
        }
    }
}
