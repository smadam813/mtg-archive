using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CollectionStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_card_oracle_id",
                table: "card");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_card_oracle_id",
                table: "card",
                column: "oracle_id");

            migrationBuilder.CreateTable(
                name: "collection_card",
                columns: table => new
                {
                    oracle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_card", x => x.oracle_id);
                    table.ForeignKey(
                        name: "fk_collection_card_card_oracle_id",
                        column: x => x.oracle_id,
                        principalTable: "card",
                        principalColumn: "oracle_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_import",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    format = table.Column<string>(type: "text", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    imported_rows = table.Column<int>(type: "integer", nullable: false),
                    quarantined_rows = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_import", x => x.id);
                    table.CheckConstraint("ck_collection_import_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "collection_import_row",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    oracle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    provenance = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_import_row", x => x.id);
                    table.ForeignKey(
                        name: "fk_collection_import_row_card_oracle_id",
                        column: x => x.oracle_id,
                        principalTable: "card",
                        principalColumn: "oracle_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_import_row_oracle_id",
                table: "collection_import_row",
                column: "oracle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_card");

            migrationBuilder.DropTable(
                name: "collection_import");

            migrationBuilder.DropTable(
                name: "collection_import_row");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_card_oracle_id",
                table: "card");

            migrationBuilder.CreateIndex(
                name: "ix_card_oracle_id",
                table: "card",
                column: "oracle_id",
                unique: true);
        }
    }
}
