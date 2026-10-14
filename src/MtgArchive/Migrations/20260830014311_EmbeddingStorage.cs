using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace MtgArchive.Migrations
{
    /// <inheritdoc />
    public partial class EmbeddingStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "embedding",
                table: "cr_rule_chunk",
                type: "vector(1024)",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "embedding",
                table: "cr_glossary_chunk",
                type: "vector(1024)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "card_face_document",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_id = table.Column<int>(type: "integer", nullable: false),
                    face_index = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_face_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_face_document_card_card_id",
                        column: x => x.card_id,
                        principalTable: "card",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "embedding_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    model_tag = table.Column<string>(type: "text", nullable: false),
                    dimensions = table.Column<int>(type: "integer", nullable: false),
                    prefix_set_version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_embedding_state", x => x.id);
                    table.CheckConstraint("ck_embedding_state_singleton", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_cr_rule_chunk_embedding",
                table: "cr_rule_chunk",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_cr_glossary_chunk_embedding",
                table: "cr_glossary_chunk",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_card_face_document_card_id_face_index",
                table: "card_face_document",
                columns: new[] { "card_id", "face_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_card_face_document_embedding",
                table: "card_face_document",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_face_document");

            migrationBuilder.DropTable(
                name: "embedding_state");

            migrationBuilder.DropIndex(
                name: "ix_cr_rule_chunk_embedding",
                table: "cr_rule_chunk");

            migrationBuilder.DropIndex(
                name: "ix_cr_glossary_chunk_embedding",
                table: "cr_glossary_chunk");

            migrationBuilder.DropColumn(
                name: "embedding",
                table: "cr_rule_chunk");

            migrationBuilder.DropColumn(
                name: "embedding",
                table: "cr_glossary_chunk");
        }
    }
}
