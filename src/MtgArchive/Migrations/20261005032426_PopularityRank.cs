using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class PopularityRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "popularity_rank",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    format = table.Column<string>(type: "text", nullable: false),
                    order = table.Column<string>(type: "text", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_popularity_rank", x => new { x.card_id, x.format, x.order });
                    table.ForeignKey(
                        name: "fk_popularity_rank_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "popularity_rank_staging",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    format = table.Column<string>(type: "text", nullable: false),
                    order = table.Column<string>(type: "text", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_popularity_rank_staging", x => new { x.card_id, x.format, x.order });
                    table.ForeignKey(
                        name: "fk_popularity_rank_card_staging",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "popularity_source",
                columns: table => new
                {
                    format = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "The date that the popularity order is current to: the Scryfall bulk updated_at for the edhrec source, the export window end for untapped.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_popularity_source", x => x.format);
                });

            migrationBuilder.CreateTable(
                name: "popularity_source_staging",
                columns: table => new
                {
                    format = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "The date that the popularity order is current to: the Scryfall bulk updated_at for the edhrec source, the export window end for untapped.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_popularity_source_staging", x => x.format);
                });

            migrationBuilder.CreateIndex(
                name: "ix_popularity_rank_place",
                table: "popularity_rank",
                columns: new[] { "format", "order", "rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_popularity_rank_place_staging",
                table: "popularity_rank_staging",
                columns: new[] { "format", "order", "rank" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "popularity_rank");

            migrationBuilder.DropTable(
                name: "popularity_rank_staging");

            migrationBuilder.DropTable(
                name: "popularity_source");

            migrationBuilder.DropTable(
                name: "popularity_source_staging");
        }
    }
}
