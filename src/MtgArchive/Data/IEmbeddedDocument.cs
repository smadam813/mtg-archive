using Pgvector;

namespace MtgArchive.Data;

/// <summary>
/// A row the re-embed phase fills: text in, vector out. The three
/// implementations are the three vector columns (card face documents, CR
/// rule chunks, CR glossary chunks). A null <see cref="Embedding"/> means
/// the row awaits embedding — freshly composed, freshly chunked, or nulled
/// by a model or prefix change.
/// </summary>
public interface IEmbeddedDocument
{
    int Id { get; }

    string Text { get; }

    Vector? Embedding { get; set; }
}
