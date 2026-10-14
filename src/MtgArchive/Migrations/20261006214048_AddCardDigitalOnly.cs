using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class AddCardDigitalOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "digital_only",
                table: "card",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "A digital-only card, such as an Alchemy card: Scryfall digital is true and the raw Scryfall legalities.commander value is not_legal. False when either input is missing. Computed at ingest.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "digital_only",
                table: "card");
        }
    }
}
