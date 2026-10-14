using MtgArchive.Data;

namespace MtgArchive.Embedding;

/// <summary>
/// The card document composer (GLOSSARY.md "Card document"): the exact
/// per-face text the card Search surface embeds. Lines, in this order and
/// nothing else: name, type line, oracle text with symbol notation verbatim
/// (<c>{2}{U}</c>, <c>{T}</c>), power/toughness or loyalty as a plain
/// suffix, the mana cost as its symbol string, and the card's Oracle Tags.
/// A line with no value is omitted. Rulings never appear: they are served
/// with a card hit, not embedded. The configured
/// <see cref="CardDocumentComposition"/> renders the oracle text line; the
/// other lines never change with it. Any change here is a
/// document-composition change: re-run the retrieval eval.
/// </summary>
public static class CardDocument
{
    /// <summary>One document per face, front first; a single-faced card
    /// is its own face 0. Oracle Tags are card-level, so every face carries
    /// them. Only direct taggings are embedded; inherited ancestors are a
    /// filter, not document text. The verbalizer counts a symbol it does not
    /// know in <paramref name="unknownSymbols"/>.</summary>
    public static IReadOnlyList<string> Compose(
        Card card, CardDocumentComposition composition, SymbolCounter? unknownSymbols = null)
    {
        var tags = card.OracleTags.Where(t => !t.Inherited).Select(t => t.Tag).Order(StringComparer.Ordinal).ToList();
        IEnumerable<CardFace> faces = card.Faces.Count == 0
            ? [new CardFace
            {
                FaceIndex = 0,
                Name = card.Name,
                TypeLine = card.TypeLine,
                OracleText = card.OracleText,
                Power = card.Power,
                Toughness = card.Toughness,
                Loyalty = card.Loyalty,
                ManaCost = card.ManaCost,
            }]
            : card.Faces.OrderBy(f => f.FaceIndex);
        return [.. faces.Select(f => Compose(f, tags, composition, unknownSymbols))];
    }

    private static string Compose(
        CardFace face, List<string> tags, CardDocumentComposition composition, SymbolCounter? unknownSymbols)
    {
        var lines = new List<string> { face.Name, face.TypeLine };
        var oracleText = face.OracleText is null ? null : EmbeddedOracleText(face.OracleText, composition, unknownSymbols);
        if (!string.IsNullOrEmpty(oracleText))
        {
            lines.Add(oracleText);
        }

        if (face.Power is not null || face.Toughness is not null)
        {
            lines.Add($"{face.Power}/{face.Toughness}");
        }

        if (face.Loyalty is not null)
        {
            lines.Add($"Loyalty {face.Loyalty}");
        }

        if (!string.IsNullOrEmpty(face.ManaCost))
        {
            lines.Add(face.ManaCost);
        }

        if (tags.Count > 0)
        {
            lines.Add($"Tags: {string.Join(", ", tags)}");
        }

        return string.Join('\n', lines);
    }

    private static string EmbeddedOracleText(
        string oracleText, CardDocumentComposition composition, SymbolCounter? unknownSymbols)
        => composition switch
        {
            CardDocumentComposition.Verbatim => oracleText,
            CardDocumentComposition.StripReminderText => ReminderText.Strip(oracleText),
            CardDocumentComposition.VerbalizeSymbols => SymbolVerbalizer.Verbalize(ReminderText.Strip(oracleText), unknownSymbols),
            _ => throw new ArgumentOutOfRangeException(nameof(composition), composition, null),
        };
}
