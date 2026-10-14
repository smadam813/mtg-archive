using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CorpusState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "no_op",
                table: "ingest_run",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "corpus_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    scryfall_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cr_effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    cr_text_sha256 = table.Column<string>(type: "text", nullable: true),
                    curated_file_hashes = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    last_phase_runs = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corpus_state", x => x.id);
                    table.CheckConstraint("ck_corpus_state_singleton", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corpus_state");

            migrationBuilder.DropColumn(
                name: "no_op",
                table: "ingest_run");
        }
    }
}
