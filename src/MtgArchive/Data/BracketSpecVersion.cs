namespace MtgArchive.Data;

/// <summary>
/// One dated bracket spec version, as the bracket load phase replayed it
/// from <c>data/bracket-spec/&lt;date&gt;.json</c>. The banlist-events
/// pattern applies: the file is truth, the table is derived, and a re-run
/// replays it. The barometer definitions and per-bracket thresholds stay in
/// <see cref="Document"/> as the file's own JSON. The spec's shape changes
/// often, and those changes stay out of the table schema. The classifier
/// parses the document at request time.
/// </summary>
public sealed class BracketSpecVersion
{
    /// <summary>The announcement date — the version's wire address.</summary>
    public required DateOnly Date { get; set; }

    public required string Source { get; set; }

    /// <summary>The file verbatim (jsonb).</summary>
    public required string Document { get; set; }

    public required int IngestRunId { get; set; }
}
