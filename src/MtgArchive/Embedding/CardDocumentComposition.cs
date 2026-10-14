namespace MtgArchive.Embedding;

/// <summary>
/// How the service renders the oracle text line of a card document before
/// it embeds it (the ablation cells). The served oracle text does
/// not change: only the embedded document does. <see cref="EmbeddingModel"/>
/// holds the composition as part of its prefix-set version, so a switch
/// closes the semantic-query gate until a re-embed.
/// </summary>
public enum CardDocumentComposition
{
    /// <summary>Oracle text verbatim, symbols and reminder text included
    /// (ADR 0007's frozen tuple).</summary>
    Verbatim,

    /// <summary>Cell A: parenthetical reminder text removed
    /// (<see cref="ReminderText"/>).</summary>
    StripReminderText,

    /// <summary>Cell B: cell A plus mana and tap symbols rendered as words
    /// (<see cref="SymbolVerbalizer"/>). The mana-cost line keeps its
    /// symbols.</summary>
    VerbalizeSymbols,
}

public static class CardDocumentCompositions
{
    public const string VerbatimName = "verbatim";

    public const string StripReminderTextName = "strip-reminder-text";

    public const string VerbalizeSymbolsName = "verbalize-symbols";

    /// <summary>The <c>Embedding:Composition</c> vocabulary, closed at three names.</summary>
    public static readonly IReadOnlyList<string> Names = [VerbatimName, StripReminderTextName, VerbalizeSymbolsName];

    public static string NameOf(CardDocumentComposition composition) => composition switch
    {
        CardDocumentComposition.Verbatim => VerbatimName,
        CardDocumentComposition.StripReminderText => StripReminderTextName,
        CardDocumentComposition.VerbalizeSymbols => VerbalizeSymbolsName,
        _ => throw new ArgumentOutOfRangeException(nameof(composition), composition, "Unknown card document composition."),
    };

    /// <summary>An absent setting is the frozen default; an unknown name
    /// fails startup here rather than at the first document.</summary>
    public static CardDocumentComposition Resolve(string? name) => name switch
    {
        null or VerbatimName => CardDocumentComposition.Verbatim,
        StripReminderTextName => CardDocumentComposition.StripReminderText,
        VerbalizeSymbolsName => CardDocumentComposition.VerbalizeSymbols,
        _ => throw new ArgumentOutOfRangeException(
            nameof(name), name, $"Unknown card document composition; Embedding:Composition must be one of: {string.Join(", ", Names)}."),
    };
}
