using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class StapleSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authoring_queue",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: true),
                    edhrec_rank = table.Column<int>(type: "integer", nullable: true),
                    lanes = table.Column<string[]>(type: "text[]", nullable: false),
                    cut_version = table.Column<string>(type: "text", nullable: true),
                    missing_capability = table.Column<string>(type: "text", nullable: true),
                    parked_on = table.Column<DateOnly>(type: "date", nullable: true)
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
                    position = table.Column<int>(type: "integer", nullable: true),
                    edhrec_rank = table.Column<int>(type: "integer", nullable: true),
                    lanes = table.Column<string[]>(type: "text[]", nullable: false),
                    cut_version = table.Column<string>(type: "text", nullable: true),
                    missing_capability = table.Column<string>(type: "text", nullable: true),
                    parked_on = table.Column<DateOnly>(type: "date", nullable: true)
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
                name: "staple_definition",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    oracle_sha256 = table.Column<string>(type: "text", nullable: false),
                    ingest_run_id = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_authoring_queue_position",
                table: "authoring_queue",
                column: "position");

            migrationBuilder.CreateIndex(
                name: "ix_authoring_queue_position_staging",
                table: "authoring_queue_staging",
                column: "position");

            migrationBuilder.CreateIndex(
                name: "ux_staple_definition_slug",
                table: "staple_definition",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authoring_queue");

            migrationBuilder.DropTable(
                name: "authoring_queue_staging");

            migrationBuilder.DropTable(
                name: "staple_definition");
        }
    }
}
