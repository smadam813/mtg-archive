using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class SourceProfileOpponentDependent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "opponent_dependent",
                table: "source_profile_staging",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "opponent_dependent",
                table: "source_profile",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Nonland sources only: true when every mana-producing ability depends on another player or on combat. Tier 1 has no opponents, so it lists the source as excluded and does not count it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "opponent_dependent",
                table: "source_profile_staging");

            migrationBuilder.DropColumn(
                name: "opponent_dependent",
                table: "source_profile");
        }
    }
}
