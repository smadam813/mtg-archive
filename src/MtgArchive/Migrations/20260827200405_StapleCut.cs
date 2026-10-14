using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class StapleCut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staple_cut_version",
                columns: table => new
                {
                    version = table.Column<string>(type: "text", nullable: false, comment: "Cut version identity as echoed in versions blocks: \"yyyy-MM-dd.<ingest_run_id>\" — dated, unique per ingest run."),
                    cut_date = table.Column<DateOnly>(type: "date", nullable: false),
                    ingest_run_id = table.Column<int>(type: "integer", nullable: false),
                    rank_cutoff = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    cut_date = table.Column<DateOnly>(type: "date", nullable: false),
                    ingest_run_id = table.Column<int>(type: "integer", nullable: false),
                    rank_cutoff = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staple_cut_version_staging", x => x.version);
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
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staple_cut_entry",
                columns: table => new
                {
                    version = table.Column<string>(type: "text", nullable: false),
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    lanes = table.Column<string[]>(type: "text[]", nullable: false),
                    edhrec_rank = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staple_cut_entry", x => new { x.version, x.card_id });
                    table.ForeignKey(
                        name: "fk_staple_cut_entry_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    lanes = table.Column<string[]>(type: "text[]", nullable: false),
                    edhrec_rank = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staple_cut_entry_staging", x => new { x.version, x.card_id });
                    table.ForeignKey(
                        name: "fk_staple_cut_entry_card_staging",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staple_cut_entry_version_staging",
                        column: x => x.version,
                        principalTable: "staple_cut_version_staging",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staple_cut_entry_card_id",
                table: "staple_cut_entry",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_staple_cut_entry_card_id_staging",
                table: "staple_cut_entry_staging",
                column: "card_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staple_cut_entry");

            migrationBuilder.DropTable(
                name: "staple_cut_entry_staging");

            migrationBuilder.DropTable(
                name: "staple_demand");

            migrationBuilder.DropTable(
                name: "staple_cut_version");

            migrationBuilder.DropTable(
                name: "staple_cut_version_staging");
        }
    }
}
