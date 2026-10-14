using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class IngestRunFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "has_partner",
                table: "card",
                type: "boolean",
                nullable: false,
                comment: "The plain Partner keyword only — not Partner With, and not a named Partner—variant (Friends forever, Survivors, …), which Scryfall also lists under the \"Partner\" keyword.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Exactly the keyword \"Partner\", not \"Partner with\".");

            migrationBuilder.AddColumn<bool>(
                name: "stale",
                table: "card",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Absent from the latest Oracle snapshot. Never deleted — excluded from legal_candidates and search, retained so collection rows and ban history never dangle.");

            migrationBuilder.CreateTable(
                name: "ingest_run",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    status = table.Column<string>(type: "text", nullable: false),
                    force = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    report = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingest_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingest_run_phase",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ingest_run_id = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    phase = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingest_run_phase", x => x.id);
                    table.ForeignKey(
                        name: "fk_ingest_run_phase_ingest_runs_ingest_run_id",
                        column: x => x.ingest_run_id,
                        principalTable: "ingest_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ingest_run_phase_ingest_run_id_phase",
                table: "ingest_run_phase",
                columns: new[] { "ingest_run_id", "phase" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ingest_run_phase");

            migrationBuilder.DropTable(
                name: "ingest_run");

            migrationBuilder.DropColumn(
                name: "stale",
                table: "card");

            migrationBuilder.AlterColumn<bool>(
                name: "has_partner",
                table: "card",
                type: "boolean",
                nullable: false,
                comment: "Exactly the keyword \"Partner\", not \"Partner with\".",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "The plain Partner keyword only — not Partner With, and not a named Partner—variant (Friends forever, Survivors, …), which Scryfall also lists under the \"Partner\" keyword.");
        }
    }
}
