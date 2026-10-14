using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class LegalityFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_legality_staging",
                table: "legality_staging");

            migrationBuilder.DropIndex(
                name: "ix_legality_version_role_staging",
                table: "legality_staging");

            migrationBuilder.DropPrimaryKey(
                name: "pk_legality",
                table: "legality");

            migrationBuilder.DropIndex(
                name: "ix_legality_version_role",
                table: "legality");

            migrationBuilder.AddColumn<string>(
                name: "format",
                table: "legality_staging",
                type: "text",
                nullable: false,
                defaultValue: "commander");

            migrationBuilder.AddColumn<string>(
                name: "format",
                table: "legality",
                type: "text",
                nullable: false,
                defaultValue: "commander");

            migrationBuilder.AddPrimaryKey(
                name: "pk_legality_staging",
                table: "legality_staging",
                columns: new[] { "card_id", "format", "banlist_version_id", "role" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_legality",
                table: "legality",
                columns: new[] { "card_id", "format", "banlist_version_id", "role" });

            migrationBuilder.CreateIndex(
                name: "ix_legality_version_format_role_staging",
                table: "legality_staging",
                columns: new[] { "banlist_version_id", "format", "role", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_legality_version_format_role",
                table: "legality",
                columns: new[] { "banlist_version_id", "format", "role", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_legality_staging",
                table: "legality_staging");

            migrationBuilder.DropIndex(
                name: "ix_legality_version_format_role_staging",
                table: "legality_staging");

            migrationBuilder.DropPrimaryKey(
                name: "pk_legality",
                table: "legality");

            migrationBuilder.DropIndex(
                name: "ix_legality_version_format_role",
                table: "legality");

            migrationBuilder.DropColumn(
                name: "format",
                table: "legality_staging");

            migrationBuilder.DropColumn(
                name: "format",
                table: "legality");

            migrationBuilder.AddPrimaryKey(
                name: "pk_legality_staging",
                table: "legality_staging",
                columns: new[] { "card_id", "banlist_version_id", "role" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_legality",
                table: "legality",
                columns: new[] { "card_id", "banlist_version_id", "role" });

            migrationBuilder.CreateIndex(
                name: "ix_legality_version_role_staging",
                table: "legality_staging",
                columns: new[] { "banlist_version_id", "role", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_legality_version_role",
                table: "legality",
                columns: new[] { "banlist_version_id", "role", "status" });
        }
    }
}
