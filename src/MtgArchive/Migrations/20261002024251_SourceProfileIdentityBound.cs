using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class SourceProfileIdentityBound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "identity_bound",
                table: "source_profile_staging",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "identity_bound",
                table: "source_profile",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "True when every any-color ability of a five-color producer binds it to the commander's color identity or to lands the players control. Tier 1 clips the source's colors to the deck's color identity.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "identity_bound",
                table: "source_profile_staging");

            migrationBuilder.DropColumn(
                name: "identity_bound",
                table: "source_profile");
        }
    }
}
