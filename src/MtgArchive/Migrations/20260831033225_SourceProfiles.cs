using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class SourceProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_profile",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    produced_colors = table.Column<string[]>(type: "text[]", nullable: true),
                    fetch_land_types = table.Column<string[]>(type: "text[]", nullable: true, comment: "Fetchable land TYPES only (empty = any land, with fetch_basic_only for the Basic constraint); null = not a fetch. Color resolution happens per deck at query time, never at ingest."),
                    fetch_basic_only = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_profile", x => x.card_id);
                    table.ForeignKey(
                        name: "fk_source_profile_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_profile_staging",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    produced_colors = table.Column<string[]>(type: "text[]", nullable: true),
                    fetch_land_types = table.Column<string[]>(type: "text[]", nullable: true),
                    fetch_basic_only = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_profile_staging", x => x.card_id);
                    table.ForeignKey(
                        name: "fk_source_profile_card_staging",
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
                name: "source_profile");

            migrationBuilder.DropTable(
                name: "source_profile_staging");
        }
    }
}
