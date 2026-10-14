using MtgArchive.Data;

namespace MtgArchive.Simulation;

/// <summary>
/// The per-format values a simulated game plays under (glossary: Game
/// rules; ADR 0012). The format fixes them; a request never sets them.
/// Tier 1 and tier 2 read the same record: tier 1 counts cards seen from
/// <see cref="StartingPlayerDraws"/>, and the engine plays it.
/// </summary>
/// <param name="StartLife">The player's start life.</param>
/// <param name="CommanderDamage">True when 21 combat damage from one commander loses the game (CR 903.10a).</param>
/// <param name="StartingPlayerDraws">True when the starting player draws on turn 1 (CR 103.8c); false on the play (CR 103.8a).</param>
/// <param name="FreeMulligans">The mulligans that cost no card (CR 103.5d).</param>
public sealed record GameRules(int StartLife, bool CommanderDamage, bool StartingPlayerDraws, int FreeMulligans)
{
    /// <summary>Commander: 40 life, commander damage, a turn-1 draw, one free mulligan.</summary>
    public static readonly GameRules Commander = new(StartLife: 40, CommanderDamage: true, StartingPlayerDraws: true, FreeMulligans: 1);

    /// <summary>Brawl and Standard Brawl: 25 life, no commander damage, on the play, one free mulligan.</summary>
    public static readonly GameRules Brawl = new(StartLife: 25, CommanderDamage: false, StartingPlayerDraws: false, FreeMulligans: 1);

    /// <summary>Competitive Brawl: Brawl with no free mulligan.</summary>
    public static readonly GameRules CompetitiveBrawl = Brawl with { FreeMulligans = 0 };

    /// <summary>The seven-card hands the mulligan policies can look at
    /// before they keep a hand of five: the opener, one more per free
    /// mulligan, and the hands for a keep of six and of five.</summary>
    private const int LooksWithoutFreeMulligans = 3;

    /// <summary>The game rules of a format (spec §1).</summary>
    public static GameRules For(Format format) => format switch
    {
        Format.Commander => Commander,
        Format.Brawl or Format.StandardBrawl => Brawl,
        Format.CompetitiveBrawl => CompetitiveBrawl,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format has no game rules."),
    };

    /// <summary>L: the most seven-card hands the mulligan can look at
    /// (glossary: Reach bound). Each mulligan reshuffles, so each hand is
    /// a fresh draw.</summary>
    public int MulliganLooks => LooksWithoutFreeMulligans + FreeMulligans;
}
