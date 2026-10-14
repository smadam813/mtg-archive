using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CrCorpus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cr_document",
                columns: table => new
                {
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    full_text = table.Column<string>(type: "text", nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cr_document", x => x.effective_date);
                });

            migrationBuilder.CreateTable(
                name: "cr_glossary_chunk",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    term = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cr_glossary_chunk", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cr_rule_chunk",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cr_rule_chunk", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rules_reembed_signal",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    cr_effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rules_reembed_signal", x => x.id);
                    table.CheckConstraint("ck_rules_reembed_signal_singleton", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_cr_glossary_chunk_term",
                table: "cr_glossary_chunk",
                column: "term",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cr_rule_chunk_number",
                table: "cr_rule_chunk",
                column: "number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cr_document");

            migrationBuilder.DropTable(
                name: "cr_glossary_chunk");

            migrationBuilder.DropTable(
                name: "cr_rule_chunk");

            migrationBuilder.DropTable(
                name: "rules_reembed_signal");
        }
    }
}
