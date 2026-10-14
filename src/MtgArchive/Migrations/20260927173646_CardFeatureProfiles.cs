using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class CardFeatureProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_feature_profile",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    face_index = table.Column<int>(type: "integer", nullable: false),
                    develops = table.Column<bool>(type: "boolean", nullable: false),
                    draws = table.Column<bool>(type: "boolean", nullable: false),
                    tap_mana = table.Column<string[]>(type: "text[]", nullable: false),
                    hand_mana = table.Column<string[]>(type: "text[]", nullable: false),
                    spell_mana = table.Column<string>(type: "text", nullable: true),
                    tutor_destination = table.Column<string>(type: "text", nullable: true),
                    tutor_enters_tapped = table.Column<bool>(type: "boolean", nullable: false),
                    tutor_via = table.Column<string>(type: "text", nullable: true),
                    tutor_activation_cost = table.Column<string>(type: "text", nullable: true),
                    has_activated_ability = table.Column<bool>(type: "boolean", nullable: false),
                    unprofiled = table.Column<bool>(type: "boolean", nullable: false, comment: "True when a sentence of the face mentions draw, search or add and matches no closed template. The row then holds only the features the templates proved, unmatched_sentence names the first such sentence, the ingest report lists the face, and engine coverage lists its card."),
                    unmatched_sentence = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_feature_profile", x => new { x.card_id, x.face_index });
                    table.ForeignKey(
                        name: "fk_card_feature_profile_card",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_feature_profile_staging",
                columns: table => new
                {
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    face_index = table.Column<int>(type: "integer", nullable: false),
                    develops = table.Column<bool>(type: "boolean", nullable: false),
                    draws = table.Column<bool>(type: "boolean", nullable: false),
                    tap_mana = table.Column<string[]>(type: "text[]", nullable: false),
                    hand_mana = table.Column<string[]>(type: "text[]", nullable: false),
                    spell_mana = table.Column<string>(type: "text", nullable: true),
                    tutor_destination = table.Column<string>(type: "text", nullable: true),
                    tutor_enters_tapped = table.Column<bool>(type: "boolean", nullable: false),
                    tutor_via = table.Column<string>(type: "text", nullable: true),
                    tutor_activation_cost = table.Column<string>(type: "text", nullable: true),
                    has_activated_ability = table.Column<bool>(type: "boolean", nullable: false),
                    unprofiled = table.Column<bool>(type: "boolean", nullable: false, comment: "True when a sentence of the face mentions draw, search or add and matches no closed template. The row then holds only the features the templates proved, unmatched_sentence names the first such sentence, the ingest report lists the face, and engine coverage lists its card."),
                    unmatched_sentence = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_feature_profile_staging", x => new { x.card_id, x.face_index });
                    table.ForeignKey(
                        name: "fk_card_feature_profile_card_staging",
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
                name: "card_feature_profile");

            migrationBuilder.DropTable(
                name: "card_feature_profile_staging");
        }
    }
}
