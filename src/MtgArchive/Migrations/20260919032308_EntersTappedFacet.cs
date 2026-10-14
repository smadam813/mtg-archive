using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class EntersTappedFacet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "enters_tapped",
                table: "source_profile_staging",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "enters_tapped",
                table: "source_profile",
                type: "text",
                nullable: true,
                comment: "Lands only: untapped, tapped, or conditional. The state parses from the land faces' closed \"enters tapped\" template family. Null for a nonland and for an unmatched land. Search-only: the availability turn does not consume it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "enters_tapped",
                table: "source_profile_staging");

            migrationBuilder.DropColumn(
                name: "enters_tapped",
                table: "source_profile");
        }
    }
}
