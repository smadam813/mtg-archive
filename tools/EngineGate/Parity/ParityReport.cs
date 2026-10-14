using System.Globalization;
using System.Text;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tools.EngineGate.Parity;

/// <summary>One comparison's line of the report.</summary>
internal sealed record ComparisonRow(
    string Name,
    string Exercises,
    int Runs,
    int Baseline,
    int Candidate,
    int EngineErrors,
    int? FreshEngineErrors,
    Difference Difference,
    Difference? Fresh,
    string Verdict)
{
    public bool Passes => Verdict is Verdicts.Pass or Verdicts.PassFresh or Verdicts.Accepted;

    public string Summary() => string.Create(CultureInfo.InvariantCulture,
        $"{Name}: baseline {Baseline}/{Runs}, candidate {Candidate}/{Runs}, difference {Difference.Estimate * 100:+0.0;-0.0} pp [{Difference.Lower * 100:0.0}, {Difference.Upper * 100:0.0}], {Verdict}");
}

/// <summary>One deck's section of the report: its swaps, lands, engine coverage and cap hits.</summary>
internal sealed record DeckSection(string Name, ParityDeck Deck, EngineCoverage Coverage, int CapHits, int CapSeeds);

/// <summary><c>eval/parity/report.md</c> (spec §6.1): each comparison, both floors, the interval and the verdict.</summary>
internal static class ParityReport
{
    public static string Render(DateTimeOffset at, ParityBaseline baseline, string xmageVersion, ParityCases cases, int runs, IReadOnlyList<ComparisonRow> rows, IReadOnlyList<DeckSection> decks, AcceptedDifferences accepted)
    {
        var acceptedRows = rows.Count(r => r.Verdict == Verdicts.Accepted);
        var passed = rows.Count(r => r.Passes) - acceptedRows;
        var md = new StringBuilder();
        md.Append(CultureInfo.InvariantCulture, $"""
            # Engine regression report

            Generated {at:yyyy-MM-dd HH:mm} UTC by `EngineGate parity` (spec §6.1; see `README.md`). Candidate XMage `{xmageVersion}` against the baseline `{baseline.Engine} {baseline.Version}`, captured {baseline.Captured}. Each comparison plays {runs} games per engine at base seed {cases.BaseSeed} and horizon {cases.TurnHorizon}, with the goal-aware pilot. It passes when the 95% Newcombe interval of the difference (candidate minus baseline) contains 0. A comparison that fails plays again at the fresh seed {cases.FreshSeed} and fails only when it fails there too.

            **Verdict: {(passed == rows.Count ? $"every comparison passes ({rows.Count} of {rows.Count})" : $"{passed} of {rows.Count} comparisons pass, {acceptedRows} accepted")}.**

            | Comparison | Baseline | Candidate | Difference | 95% interval | Fresh-seed interval | Engine errors | Verdict |
            |---|---|---|---|---|---|---|---|

            """);
        foreach (var r in rows)
        {
            md.Append(CultureInfo.InvariantCulture,
                $"| {r.Name} | {Percent(r.Baseline, r.Runs)} | {Percent(r.Candidate, r.Runs)} | {r.Difference.Estimate * 100:+0.0;-0.0} pp | {Interval(r.Difference)} | {(r.Fresh is { } f ? Interval(f) : "–")} | {r.EngineErrors}{(r.FreshEngineErrors is { } fe ? $"; fresh {fe}" : string.Empty)} | {r.Verdict} |\n");
        }

        if (accepted.Accepted.Count > 0)
        {
            md.Append("\n## Accepted differences\n\n");
            md.Append("Each comparison below fails on both seeds. The user accepted it for the cause its evidence shows. A defect on the candidate side is fixed, not accepted.\n\n");
            md.Append("| Comparison | Direction | Verdict | Cause | Evidence |\n|---|---|---|---|---|\n");
            foreach (var a in accepted.Accepted)
            {
                var verdict = rows.FirstOrDefault(r => r.Name == a.Comparison)?.Verdict ?? "not run";
                var note = verdict is Verdicts.Pass or Verdicts.PassFresh ? $"{verdict}; the entry is not needed" : verdict;
                md.Append($"| {a.Comparison} | {a.Direction} | {note} | {a.Cause} | {a.Evidence} |\n");
            }
        }

        md.Append("\n## Cases\n\n");
        foreach (var c in cases.Cases)
        {
            md.Append(CultureInfo.InvariantCulture, $"- `{c.Name}` on `{c.Deck}`: {c.ParseGoal()}{(c.Disruption is null ? string.Empty : $", disruption {c.ParseDisruption()}")}. {c.Exercises}\n");
            if (NeverMet(baseline, c.Name))
            {
                md.Append("  The baseline meets the goal in no disrupted run, so this arm checks only that the candidate applies the disruption the same way.\n");
            }
        }

        md.Append("\n## Decks\n");
        foreach (var d in decks)
        {
            var lands = d.Deck.Deck.Where(c => c.HasLandFace).Select(c => c.Quantity > 1 ? $"{c.Name} ×{c.Quantity}" : c.Name);
            md.Append(CultureInfo.InvariantCulture, $"""

                ### {d.Name}

                Captured from `{d.Deck.Source}`. {d.Deck.Substitutions.Count} cards became filler cards.

                - Filler swaps: {(d.Deck.Substitutions.Count == 0 ? "none" : string.Join("; ", d.Deck.Substitutions.Select(s => $"{s.Card} → {s.Filler}")))}.
                - Lands ({d.Deck.Deck.Where(c => c.HasLandFace).Sum(c => c.Quantity)}): {string.Join(", ", lands)}.
                - Engine coverage on XMage: unimplemented {List(d.Coverage.Unimplemented)}; unprofiled {List(d.Coverage.Unprofiled)}.
                - Activation cap (roadmap carried-forward item 3): {d.CapHits} of {d.CapSeeds} runs reach the cap of two activations per ability per turn.

                """);
        }

        md.Append("""

            ## Carried-forward checks

            - Item 3, the activation cap: see each deck above.
            - Item 4, the turn-1 draw: the turn-1 comparisons cover it. XMage's Commander duel skips that draw; the shim restores it (`GoldfishDuel`).

            """);
        return md.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>True when the baseline meets the goal in no disrupted run of the case, at either seed.</summary>
    private static bool NeverMet(ParityBaseline baseline, string @case)
    {
        var disrupted = baseline.Comparisons.Where(e => e.Case == @case && e.Arm == "disrupted").ToList();
        return disrupted.Count > 0 && disrupted.All(e => e.Successes == 0 && e.FreshSuccesses == 0);
    }

    private static string Percent(int successes, int runs) => string.Create(CultureInfo.InvariantCulture, $"{successes * 100.0 / runs:0.0}%");

    private static string Interval(Difference d) => string.Create(CultureInfo.InvariantCulture, $"[{d.Lower * 100:0.0}, {d.Upper * 100:0.0}] pp");

    private static string List(IReadOnlyList<string> names) => names.Count == 0 ? "none" : string.Join(", ", names);
}
