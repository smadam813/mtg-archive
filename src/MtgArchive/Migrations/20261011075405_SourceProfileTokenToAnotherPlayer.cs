using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class SourceProfileTokenToAnotherPlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "token_to_another_player",
                table: "source_profile_staging",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "token_to_another_player",
                table: "source_profile",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Nonland sources only. This is true when every mana-producing ability and every fetch ability creates its token for another player (\"its controller creates\", \"target opponent creates\", a Gift). Tier 1 lists the source as excluded and does not count it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "token_to_another_player",
                table: "source_profile_staging");

            migrationBuilder.DropColumn(
                name: "token_to_another_player",
                table: "source_profile");
        }
    }
}
