using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CardFeatureResponse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "response_counter_colors",
                table: "card_feature_profile_staging",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "response_counter_excluded_types",
                table: "card_feature_profile_staging",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<bool>(
                name: "response_counter_loses_game",
                table: "card_feature_profile_staging",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "response_counter_reach",
                table: "card_feature_profile_staging",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "response_counter_types",
                table: "card_feature_profile_staging",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "response_counter_unless_pays",
                table: "card_feature_profile_staging",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "response_grant_scopes",
                table: "card_feature_profile_staging",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "response_grants",
                table: "card_feature_profile_staging",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "response_unmatched_sentence",
                table: "card_feature_profile_staging",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "response_unprofiled",
                table: "card_feature_profile_staging",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "True when a sentence of the face mentions an answer and matches no response family. The row then holds only the response features the families proved, response_unmatched_sentence names the first such sentence, the ingest report lists the face, and the deck feature profile lists its card among the unprofiled responses. Separate from unprofiled.");

            migrationBuilder.AddColumn<string[]>(
                name: "response_counter_colors",
                table: "card_feature_profile",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "response_counter_excluded_types",
                table: "card_feature_profile",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<bool>(
                name: "response_counter_loses_game",
                table: "card_feature_profile",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "response_counter_reach",
                table: "card_feature_profile",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "response_counter_types",
                table: "card_feature_profile",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "response_counter_unless_pays",
                table: "card_feature_profile",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "response_grant_scopes",
                table: "card_feature_profile",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "response_grants",
                table: "card_feature_profile",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "response_unmatched_sentence",
                table: "card_feature_profile",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "response_unprofiled",
                table: "card_feature_profile",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "True when a sentence of the face mentions an answer and matches no response family. The row then holds only the response features the families proved, response_unmatched_sentence names the first such sentence, the ingest report lists the face, and the deck feature profile lists its card among the unprofiled responses. Separate from unprofiled.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "response_counter_colors",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_counter_excluded_types",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_counter_loses_game",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_counter_reach",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_counter_types",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_counter_unless_pays",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_grant_scopes",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_grants",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_unmatched_sentence",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_unprofiled",
                table: "card_feature_profile_staging");

            migrationBuilder.DropColumn(
                name: "response_counter_colors",
                table: "card_feature_profile");

            migrationBuilder.DropColumn(
                name: "response_counter_excluded_types",
                table: "card_feature_profile");

            migrationBuilder.DropColumn(
                name: "response_counter_loses_game",
                table: "card_feature_profile");

            migrationBuilder.DropColumn(
                name: "response_counter_reach",
                table: "card_feature_profile");

            migrationBuilder.DropColumn(
                name: "response_counter_types",
                table: "card_feature_profile");

            migrationBuilder.DropColumn(
                name: "response_counter_unless_pays",
                table: "card_feature_profile");

            migrationBuilder.DropColumn(
                name: "response_grant_scopes",
                table: "card_feature_profile");

            migrationBuilder.DropColumn(
                name: "response_grants",
                table: "card_feature_profile");

            migrationBuilder.DropColumn(
                name: "response_unmatched_sentence",
                table: "card_feature_profile");

            migrationBuilder.DropColumn(
                name: "response_unprofiled",
                table: "card_feature_profile");
        }
    }
}
