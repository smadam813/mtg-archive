using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CardFaceProducedColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "produced_colors",
                table: "card_face",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "produced_colors",
                table: "card_face");
        }
    }
}
