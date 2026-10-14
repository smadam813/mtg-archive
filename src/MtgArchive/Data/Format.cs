namespace MtgArchive.Data;

/// <summary>
/// The rules set a deck is built and judged under (GLOSSARY.md "Format",
/// ADR 0012). Legality is per (card, format, role); Commander adds the
/// banlist version axis. The wire names are snake_case:
/// <c>commander</c>, <c>brawl</c>, <c>competitive_brawl</c>,
/// <c>standard_brawl</c>. A request that names no format means Commander.
/// </summary>
public enum Format
{
    Commander,
    Brawl,
    CompetitiveBrawl,
    StandardBrawl,
}
