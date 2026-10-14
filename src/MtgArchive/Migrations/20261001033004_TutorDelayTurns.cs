using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class TutorDelayTurns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "tutor_delay_turns",
                table: "card_feature_profile_staging",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "tutor_delay_turns",
                table: "card_feature_profile",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tutor_delay_turns",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "tutor_delay_turns",
                table: "card_feature_profile");
        }
    }
}
