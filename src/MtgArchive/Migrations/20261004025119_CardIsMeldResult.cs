using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CardIsMeldResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "can_be_commander",
                table: "card",
                type: "boolean",
                nullable: false,
                comment: "Commander eligibility: legendary creature, or card text containing \"can be your commander\"; never a meld result. Computed at ingest.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Commander eligibility: legendary creature, or card text containing \"can be your commander\". Computed at ingest.");

            migrationBuilder.AddColumn<bool>(
                name: "is_meld_result",
                table: "card",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "The combined back of a meld pair: Scryfall all_parts names the card itself as the meld_result. Never a card of its own, so not legal in any role. Computed at ingest.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_meld_result",
                table: "card");

            migrationBuilder.AlterColumn<bool>(
                name: "can_be_commander",
                table: "card",
                type: "boolean",
                nullable: false,
                comment: "Commander eligibility: legendary creature, or card text containing \"can be your commander\". Computed at ingest.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Commander eligibility: legendary creature, or card text containing \"can be your commander\"; never a meld result. Computed at ingest.");
        }
    }
}
