using Pgvector;

namespace MtgArchive.Data;

/// <summary>
/// One face's card document (GLOSSARY.md "Card document") and its vector:
/// the per-face text the card Search surface embeds. A single-faced card has
/// one row at face 0; faces dedupe to their card at query time. The card
/// upsert phase writes the text, composed by
/// <see cref="Embedding.CardDocument"/>, in the same commit as the card;
/// <see cref="Embedding.ReEmbedPhase"/> fills the vector. A stale card has
/// no row.
/// </summary>
public sealed class CardFaceDocument : IEmbeddedDocument
{
    public int Id { get; set; }

    public int CardId { get; set; }

    /// <summary>Face order as printed; 0 is the front face, and the only
    /// face of a single-faced card.</summary>
    public required int FaceIndex { get; set; }

    /// <summary>The composed document text exactly as embedded.</summary>
    public required string Text { get; set; }

    public Vector? Embedding { get; set; }
}
