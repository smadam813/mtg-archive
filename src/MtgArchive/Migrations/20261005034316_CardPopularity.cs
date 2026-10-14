using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CardPopularity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_popularity",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    format = table.Column<string>(type: "text", nullable: false),
                    seat = table.Column<string>(type: "text", nullable: false),
                    share = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true, comment: "The export's Popularity value in percent as Untapped wrote it (4.0 for \"4.0%\"): for the main seat the share of decks that play the card, for the commander seat the share of games with the card as commander."),
                    total_games = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_popularity", x => new { x.card_id, x.format, x.seat });
                    table.ForeignKey(
                        name: "fk_card_popularity_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_popularity_staging",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    format = table.Column<string>(type: "text", nullable: false),
                    seat = table.Column<string>(type: "text", nullable: false),
                    share = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true, comment: "The export's Popularity value in percent as Untapped wrote it (4.0 for \"4.0%\"): for the main seat the share of decks that play the card, for the commander seat the share of games with the card as commander."),
                    total_games = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_popularity_staging", x => new { x.card_id, x.format, x.seat });
                    table.ForeignKey(
                        name: "fk_card_popularity_card_staging",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_popularity");

            migrationBuilder.DropTable(
                name: "card_popularity_staging");
        }
    }
}
