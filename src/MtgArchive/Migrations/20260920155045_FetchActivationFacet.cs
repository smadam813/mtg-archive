using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class FetchActivationFacet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fetch_activation",
                table: "source_profile_staging",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fetch_activation",
                table: "source_profile",
                type: "text",
                nullable: true,
                comment: "Lands only: untapped or tapped, the state the found land enters in, when the fetch is the closed \"{T}, [Pay N life,] Sacrifice this land: Search your library for <targets>, put it onto the battlefield [tapped], then shuffle\" template. Null for any other fetch shape: a mana cost, a trigger, a search to hand, a spell. The goldfish engine builds the land's one ability from it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fetch_activation",
                table: "source_profile_staging");

            migrationBuilder.DropColumn(
                name: "fetch_activation",
                table: "source_profile");
        }
    }
}
