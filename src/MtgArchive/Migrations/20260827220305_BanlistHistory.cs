using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class BanlistHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "banlist_version",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    date_precision = table.Column<string>(type: "text", nullable: false),
                    announcement_url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_banlist_version", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ban_event",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    banlist_version_id = table.Column<int>(type: "integer", nullable: false),
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ban_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_ban_event_banlist_version_banlist_version_id",
                        column: x => x.banlist_version_id,
                        principalTable: "banlist_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ban_event_card_card_id",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ban_event_banlist_version_id_card_id",
                table: "ban_event",
                columns: new[] { "banlist_version_id", "card_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ban_event_card_id",
                table: "ban_event",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ix_banlist_version_effective_date",
                table: "banlist_version",
                column: "effective_date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ban_event");

            migrationBuilder.DropTable(
                name: "banlist_version");
        }
    }
}
