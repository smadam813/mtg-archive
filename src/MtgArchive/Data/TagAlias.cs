namespace MtgArchive.Data;

/// <summary>
/// One Scryfall tag alias ("blink") and the label it names ("flicker").
/// The card query endpoints' <c>tags</c> filter resolves an alias to its
/// label before matching; card documents never carry aliases. The table is
/// replaced from the Oracle Tags file on every ingest run.
/// </summary>
public sealed class TagAlias
{
    public required string Alias { get; set; }

    public required string Label { get; set; }
}
