using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CrChunkVersionAndDocumentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "cr_effective_date",
                table: "cr_rule_chunk",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "cr_effective_date",
                table: "cr_glossary_chunk",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "full_text_sha256",
                table: "cr_document",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cr_effective_date",
                table: "cr_rule_chunk");

            migrationBuilder.DropColumn(
                name: "cr_effective_date",
                table: "cr_glossary_chunk");

            migrationBuilder.DropColumn(
                name: "full_text_sha256",
                table: "cr_document");
        }
    }
}
