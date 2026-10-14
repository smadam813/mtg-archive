using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class MaterializedLegality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legality",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    banlist_version_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legality", x => new { x.card_id, x.banlist_version_id, x.role });
                    table.ForeignKey(
                        name: "fk_legality_banlist_version",
                        column: x => x.banlist_version_id,
                        principalTable: "banlist_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_legality_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_legality_reason",
                        column: x => x.reason,
                        principalTable: "ban_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "legality_staging",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    banlist_version_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legality_staging", x => new { x.card_id, x.banlist_version_id, x.role });
                    table.ForeignKey(
                        name: "fk_legality_banlist_version_staging",
                        column: x => x.banlist_version_id,
                        principalTable: "banlist_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_legality_card_staging",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_legality_reason_staging",
                        column: x => x.reason,
                        principalTable: "ban_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_legality_reason",
                table: "legality",
                column: "reason");

            migrationBuilder.CreateIndex(
                name: "ix_legality_version_role",
                table: "legality",
                columns: new[] { "banlist_version_id", "role", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_legality_reason_staging",
                table: "legality_staging",
                column: "reason");

            migrationBuilder.CreateIndex(
                name: "ix_legality_version_role_staging",
                table: "legality_staging",
                columns: new[] { "banlist_version_id", "role", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legality");

            migrationBuilder.DropTable(
                name: "legality_staging");
        }
    }
}
