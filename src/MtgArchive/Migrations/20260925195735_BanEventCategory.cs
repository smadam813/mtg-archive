using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class BanEventCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "ban_event",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category",
                table: "ban_event");
        }
    }
}
