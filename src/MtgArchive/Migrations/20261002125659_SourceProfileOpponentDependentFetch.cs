using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class SourceProfileOpponentDependentFetch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "opponent_dependent",
                table: "source_profile",
                type: "boolean",
                nullable: false,
                comment: "Nonland sources only: true when every mana-producing ability and every fetch ability depends on another player or on combat. Tier 1 has no opponents, so it lists the source as excluded and does not count it.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Nonland sources only: true when every mana-producing ability depends on another player or on combat. Tier 1 has no opponents, so it lists the source as excluded and does not count it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "opponent_dependent",
                table: "source_profile",
                type: "boolean",
                nullable: false,
                comment: "Nonland sources only: true when every mana-producing ability depends on another player or on combat. Tier 1 has no opponents, so it lists the source as excluded and does not count it.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Nonland sources only: true when every mana-producing ability and every fetch ability depends on another player or on combat. Tier 1 has no opponents, so it lists the source as excluded and does not count it.");
        }
    }
}
