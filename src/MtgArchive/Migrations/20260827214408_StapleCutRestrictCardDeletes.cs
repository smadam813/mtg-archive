using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class StapleCutRestrictCardDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_staple_cut_entry_card",
                table: "staple_cut_entry");

            migrationBuilder.DropForeignKey(
                name: "fk_staple_cut_entry_card_staging",
                table: "staple_cut_entry_staging");

            migrationBuilder.DropForeignKey(
                name: "fk_staple_demand_card_card_id",
                table: "staple_demand");

            migrationBuilder.AddForeignKey(
                name: "fk_staple_cut_entry_card",
                table: "staple_cut_entry",
                column: "card_id",
                principalTable: "card",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staple_cut_entry_card_staging",
                table: "staple_cut_entry_staging",
                column: "card_id",
                principalTable: "card",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staple_demand_card_card_id",
                table: "staple_demand",
                column: "card_id",
                principalTable: "card",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_staple_cut_entry_card",
                table: "staple_cut_entry");

            migrationBuilder.DropForeignKey(
                name: "fk_staple_cut_entry_card_staging",
                table: "staple_cut_entry_staging");

            migrationBuilder.DropForeignKey(
                name: "fk_staple_demand_card_card_id",
                table: "staple_demand");

            migrationBuilder.AddForeignKey(
                name: "fk_staple_cut_entry_card",
                table: "staple_cut_entry",
                column: "card_id",
                principalTable: "card",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_staple_cut_entry_card_staging",
                table: "staple_cut_entry_staging",
                column: "card_id",
                principalTable: "card",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_staple_demand_card_card_id",
                table: "staple_demand",
                column: "card_id",
                principalTable: "card",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
