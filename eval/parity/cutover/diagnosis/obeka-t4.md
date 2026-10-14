# Obeka turn-4 gap: diagnosis

The phase 3C parity gate found `dq2-obeka-t4` at 75.2% on our engine and 51.8% on XMage (`../report.md`). This diagnosis measures what explains the gap. Each row is 10,000 goal-aware runs at base seed 20260928; each interval is the 95% Newcombe interval against x0.

| Label | Engine | Change | Floor | Against x0 |
|---|---|---|---|---|
| x0 | XMage | ETB lines only | 5178/10000 (51.8%) [50.8, 52.8] | – |
| x1 | XMage | pays 2 life for shocklands | 5178/10000 (51.8%) [50.8, 52.8] | +0.0 pp [−1.4, 1.4] |
| o0 | ours | none | 7520/10000 (75.2%) [74.3, 76.0] | +23.4 pp [22.1, 24.7] |
| o1 | ours | lands in name order | 7996/10000 (80.0%) [79.2, 80.7] | +28.2 pp [26.9, 29.4] |
| o2 | ours | enters tapped: Sunken Hollow | 7220/10000 (72.2%) [71.3, 73.1] | +20.4 pp [19.1, 21.7] |
| o3 | ours | land rows as XMage reads them | 6812/10000 (68.1%) [67.2, 69.0] | +16.3 pp [15.0, 17.7] |
| o4 | ours | o1 + o2 + o3 | 6932/10000 (69.3%) [68.4, 70.2] | +17.5 pp [16.2, 18.9] |

All seven traces read the frozen deck `dq2-obeka.json` (SHA-256 `5e168e43…`). XMage is `xmage-1.4.61V1+34746b9c`; our engine is `1.0.0+0b2e07f`. x0 gives the phase 3C floor of 51.8%, so the ETB switch does not change play. o0 gives the phase 3C baseline of 7520.

## How to read the failure classes

The trace puts each XMage failure in one class from its board at the end of turn 4. x0 has 2622 LandShort, 332 ColorShort and 1868 RulesShort failures.

- **LandShort** means less than 4 mana on the battlefield, 4 being Obeka's mana value. A land counts 1 and Sol Ring counts 2. Dark Ritual and Simian Spirit Guide work from hand, so they do not count.
- **ColorShort** means 4 or more mana, but no source for each of U, B and R. Each land pays one pip, so a dual land pays one of its colors, not both.
- **RulesShort** means 4 or more mana and a source for each pip by the rows, so a land rule or the payment kept the cast out.
- Our engine writes no end-of-turn line, so all its failures have class Unknown.

The failures files keep the first 20 failures of each class. Every count below that comes from them is a count out of 20. Such a count gives a wide interval: 6 of 20 is 30%, with a 95% Wilson interval of 15% to 52%.

## Findings

**H1, lands that enter tapped.** On x0, Sunken Hollow enters tapped in 2570 of 2824 entries (91%). The checklands enter tapped less often: Dragonskull Summit 810 of 1910 (42%), Drowned Catacomb 746 of 1837 (41%), Sulfur Falls 330 of 1877 (18%). Every other land always enters untapped, the shocklands included. Decision 4's set is only Sunken Hollow; the set uses x0's counts, because x1 closes no more of the gap than x0. o2 lowers our floor by 3.0 pp, so H1 explains 3.0 pp. The gap of o2 against x0 is +20.4 pp [19.1, 21.7]. The checklands fall below the cut, so no ablation measures them. Their lift on x0 shows no harm: runs with Drowned Catacomb fail in 30%, below the 48% average.

**H2, XMage declines the shockland life.** Rejected. On x0, all 10,290 shockland entries are untapped: XMage's own player already pays the 2 life. x1 logs 10,290 `DIAG pay-life` lines, so its switch works. x1 gives the same 5178 successes as x0: +0.0 pp [−1.4, 1.4]. H2 explains 0 pp.

**H3, filter lands and Verges.** o3 lowers our floor by 7.1 pp. o3 is an upper bound on H3, not a measurement. It makes each filter land tap for {C} only and each Verge for its first color only. On XMage, a filter land still makes colored mana when another land pays its hybrid cost. A Verge's second color is live when you control a land of the stated type, and this deck has many such lands. The failures agree with the bound: 8 of the 20 RulesShort runs tried the cast and failed where only a filter-land chain could pay (runs 24, 37, 55, 56, 61, 71, 73, 94). 3 more never tried, because only a chain or a Verge's second color could make a needed color (runs 19, 20 and 74). Runs 37 and 74 also sacrificed Fiery Islet, and each held Underground Sea behind a land with an earlier name. Either land would have paid directly. So H3 explains up to 7.1 pp.

**H4, the land order.** Our engine plays lands in draw order; XMage plays them in name order. o1 widens the gap by 4.8 pp, from 23.4 to 28.2. The land order is a real difference, but it favors our engine, not XMage. The main cause is Windswept Heath (see below). Name order plays Windswept Heath near the end, so our engine plays it in 441 runs instead of 931 and loses fewer land drops. The effect means that the other causes are larger than 23.4 pp: about 28 pp of XMage-only failures sit behind the draw-order advantage. On XMage, name order also picks a bad land over a good one in hand. Runs 0 and 12 play Sunken Hollow, which enters tapped, and hold an untapped land. Runs 73, 74, 93 and 94 play a land that lacks the needed color and hold one that has it. Name order explains 7 of the 20 ColorShort runs. In 5 of them, the pilot holds a land of the missing color and plays a land with an earlier name (runs 93, 128, 150, 211 and 404). The held land would pay for Obeka on turn 4. In 2 more, a fetch land takes the first land by name, which lacks the missing color, and a land with that color stays in the library (runs 251 and 549).

**H5, XMage's own payment.** Supported. `PILOT cannot cast_commander Obeka` means that XMage offered the cast and its own payment then failed. In 5 of the 20 RulesShort runs, the lands and Sol Ring could pay Obeka directly, with no filter chain: runs 5, 6, 9, 16 and 76. Each of these payments needs a flexible source for one part: Sol Ring, Urza's Saga, Command Tower, Mana Confluence or a filter land's {C}. A guess: XMage spends such a source on the wrong cost part (roadmap item 22). In 1 more run (103), the pilot tried the cast before its land drop. XMage's payment failed with Simian Spirit Guide in hand. The shim then blocks a second try in that turn, so the pilot did not cast Obeka after it played City of Brass. No ablation measures H5. The sample puts it at 6 of 20 RulesShort runs: about 5.6 pp, with an interval of 2.7 to 9.7 pp.

**H6, Urza's Saga.** Supported, and small. On x0, runs with Urza's Saga by turn 4 fail in 70.3% (473 of 673); on o0 they fail in 30.2% (308 of 1020). Run 21 shows the cause: chapter III sacrifices a Saga played on turn 2, and the run ends turn 4 with 3 lands. The failures show it in 1 of 20 LandShort runs. A land's lift on one engine is (failure rate of the runs with the land − failure rate of the runs without it) × the runs with the land ÷ 100, in pp; the lift difference is XMage's lift minus our engine's. Against o0, Urza's Saga's lift difference is 1.59 − 0.61 ≈ 1.0 pp. This estimate is a correlation, not a measurement.

**Windswept Heath.** Both engines lose a land drop to it. It fetches a Forest or a Plains, and the deck has neither. Runs with it fail in 91.0% on x0 (446 of 490) and 86.1% on o0 (802 of 931). Our engine plays it about twice as often, because it plays lands in draw order. In o0, 8 of the 20 kept failures lost the drop to it; in o4, 2 of 20. Measured by lift, Windswept Heath costs our engine about 6.3 pp and XMage about 2.2 pp. So it narrows the o0 gap by about 4 pp, and o1 removes most of that. This is a fault of the deck, not of an engine.

## The part left over

o4 leaves 17.5 pp [16.2, 18.9]. The failures and the lift tables name four causes that no ablation models. All four are XMage-only.

1. **Fiery Islet (LandShort).** XMage runs its ability "{1}, {T}, Sacrifice: Draw a card", and the pilot activates it. Our engine has no such ability for this land. The sacrifice changed the result in 6 of the 20 LandShort runs (runs 10, 27, 47, 66, 79, 86). Run 4 also sacrificed Fiery Islet, but its four lands without the sacrifice (Fiery Islet, Steam Vents, Island, Cascade Bluffs) make no black, so it fails either way. The sacrifice also shows in other classes: runs 37 and 74 (RulesShort) and run 426 (ColorShort). On x0, runs with Fiery Islet fail in 82.7% (1073 of 1297); on o4, in 20.9% (262 of 1256). The lift difference against o4 is 5.14 − (−1.41) ≈ 6.6 pp. The LandShort sample alone gives 7.9 pp, with an interval of 3.8 to 13.6.
2. **XMage's payment (RulesShort).** H5 above: about 5.6 pp [2.7, 9.7].
3. **Casts in the upkeep (RulesShort).** XMage gives the pilot priority in the upkeep, and the pilot casts there. Our engine casts only in the main phase. In run 60, an upkeep Brainstorm tapped the only blue source, so Obeka could not be cast. In run 5, an upkeep Dark Ritual made {B}{B}{B} that emptied before the main phase; run 5 also counts under H5. The sample puts this at 1 of 20 RulesShort runs: about 0.9 pp [0.2, 4.4].
4. **Urza's Saga (LandShort).** H6 above. On o4, runs with Urza's Saga already fail in 50.4% (350 of 695), so the lift difference against o4 is 1.59 − 1.47 ≈ 0.1 pp: about 0.

These estimates add to about 13.2 pp (6.6 + 5.6 + 0.9 + 0.1). The rest, about 4.3 pp, has no named cause. The LandShort runs also show the draw: 11 of the 20 had no land in hand for a drop (runs 1, 7, 31, 33, 36, 43, 49, 54, 65, 77, 78). The draw has the same distribution on both engines, so it is no cause of the gap. Run 3 lost its turn-2 land drop to Windswept Heath (see Windswept Heath above). No run shows a pilot that held a land and skipped its drop. The three runs with a mulligan (21, 27 and 78) have another cause.

The 332 ColorShort runs, 3.3 pp of x0, name no new cause. 7 of the 20 kept runs fail by name order, which o1 and o4 already give our engine (H4). 4 never draw a source of the missing color (runs 68, 131, 267 and 684). The other 9 have 2 or 3 lands and Sol Ring: enough mana, but too few colored sources (runs 29, 169, 241, 426, 428, 452, 570, 616 and 706). In one of them (426), the Fiery Islet sacrifice took the missing color; cause 1 already counts it. The draw has the same distribution on both engines.

## Verdict

Not explained. By Decision 3, o4 against x0 is +17.5 pp [16.2, 18.9], and the interval does not contain 0. The measured causes are H1 (3.0 pp), H3 (at most 7.1 pp) and the ablations together (5.9 pp, o0 to o4). H2 explains nothing, and H4 widens the gap by 4.8 pp. o3 is an upper bound on H3, so 17.5 pp is a lower bound on the part left over. The 17.5 pp left over fall in two classes. LandShort holds Fiery Islet (about 6.6 pp) and Urza's Saga (about 0). RulesShort holds XMage's payment (about 5.6 pp) and casts in the upkeep (about 0.9 pp). ColorShort holds no cause that o4 does not already model. About 4.3 pp have no named cause.

## Recommendation

The user decides. For each cause, the options are:

1. **Fiery Islet.** (a) Give our engine Fiery Islet's draw ability, so both engines offer the pilot the same choice. (b) Change the pilot policy so that it does not sacrifice a land for a card before the goal turn. (c) Accept the gap and replace Fiery Islet in the parity deck.
2. **XMage's payment (H5).** (a) Change the shim to pay with our engine's maximum matching, not with XMage's own payment (roadmap item 22). (b) Change the shim to allow a second try after a land drop (run 103). (c) Accept the gap and change the gate case.
3. **Casts in the upkeep.** (a) Change the shim to offer the pilot actions in the main phase only, as our engine does. (b) Change the pilot policy so that it does not cast in the upkeep. (c) Accept the gap.
4. **Filter lands and Verges (H3).** (a) Fix our engine to model the hybrid payment of filter lands and the Verge condition. (b) Change the shim's payment to chain filter lands. (c) Accept the gap and replace these lands in the parity deck.
5. **Lands that enter tapped (H1).** (a) Fix our engine to model the enters-tapped state of Sunken Hollow and the checklands with their conditions. (b) Accept the gap.
6. **Land order (H4).** (a) Give both engines the same action order. (b) Change the pilot policy to prefer an untapped land of a needed color over the first land in the list. Either option also changes the Windswept Heath effect. (c) Accept the difference.
7. **Urza's Saga (H6).** (a) Give Urza's Saga a card feature profile in our engine. (b) Accept the gap and replace it in the parity deck.
8. **Windswept Heath.** (a) Remove it from the parity deck, or replace it with a filler card, because it finds nothing in this deck. (b) Change the pilot policy so that it does not play a fetch whose land types the decklist does not hold.

## Phase 3E

Phase 3E applied two of the options above. Task 2 is option 2(a): the shim pays by maximum matching. It also makes a source such as Command Tower add the color it was matched for, and it skips conditional mana that does not apply. Task 3 is option 1(b): the shared pilot does not spend a land to draw before the goal turn. x2 has the payment fix only; x3 has both. Both run on image `mtg-archive-xmage:3e` (`xmage-1.4.61V1+96331334`).

| Label | Change | Floor | Against x0 | Against o0 | Against o4 |
|---|---|---|---|---|---|
| x0 | ETB lines only | 5178 (51.8%) [50.8, 52.8] | – | −23.4 pp [−24.7, −22.1] | −17.5 pp [−18.9, −16.2] |
| x2 | payment fix | 6037 (60.4%) [59.4, 61.3] | +8.6 pp [7.2, 10.0] | −14.8 pp [−16.1, −13.5] | −9.0 pp [−10.3, −7.6] |
| x3 | payment fix + pilot rule | 6028 (60.3%) [59.3, 61.2] | +8.5 pp [7.1, 9.9] | −14.9 pp [−16.2, −13.6] | −9.0 pp [−10.4, −7.7] |
| x4 | x3 + shim retry and guide (Task 6) + goal-met draw rule (Task 7) | 6885 (68.9%) [67.9, 69.8] | +17.1 pp [15.7, 18.4] | −6.3 pp [−7.6, −5.1] | −0.5 pp [−1.8, 0.8] |

x3 against x2 is −0.1 pp [−1.4, 1.3].

| Label | LandShort | ColorShort | RulesShort | Fiery Islet runs | Fiery Islet failures |
|---|---|---|---|---|---|
| x0 | 2622 | 332 | 1868 | 1297 | 1073 (82.7%) |
| x2 | 2410 | 331 | 1222 | 1295 | 890 (68.7%) |
| x3 | 2413 | 327 | 1232 | 1295 | 899 (69.4%) |
| x4 | 1730 | 322 | 1063 | 1295 | 288 (22.2%) |
| o4 | – | – | – | 1256 | 262 (20.9%) |

x3 logs 2402 failed cast tries (`cannot_cast`, all runs, all turns to the horizon). x0 and x2 came before Task 4, so their summaries have no `cannot_cast` and no `paths`. Only the failures files can compare them: they hold 16 (x0), 15 (x2) and 16 (x3) "PILOT cannot cast" lines in turns 1 to 4. These counts come from 60 kept failures each, not from all runs. The runs kept differ between the labels, so the counts do not measure the payment fix.

**The payment fix closed 8.6 pp** [7.2, 10.0] (x2 against x0). It took RulesShort from 1868 to 1222. This is more than H5's sample estimate of 5.6 pp. The fix also lowered the Fiery Islet failure rate from 82.7% to 68.7%, because many runs with Fiery Islet also failed on the payment.

**The pilot rule closed nothing** (x3 against x2). The rule stops a land draw on turns 1 to 3. The pilot now keeps Fiery Islet to turn 4 and sacrifices it in the turn-4 upkeep, before the cast. The goal turn is not before the goal turn, so the rule allows it. All 9 sacrifices in x3's failures come in the turn-4 upkeep; x0's 11 come on turns 2 to 4. The Fiery Islet failure rate stays at 69.4%, not near o4's 20.9%. In x3's failures, the upkeep sacrifice changes the result in runs 27, 66 and 86 (LandShort), 37 and 74 (RulesShort) and 426 (ColorShort). Run 27 shows it: the pilot holds Fiery Islet through turn 3 and sacrifices it in the turn-4 upkeep. It then plays Drowned Catacomb and ends with three lands. With Fiery Islet, its four lands pay {1}{U}{B}{R}.

### The causes left

x3 against o4 is −9.0 pp [−10.4, −7.7]. o4 already gives our engine the land order (H4), Sunken Hollow tapped (H1) and the upper bound of the filter lands and Verges (H3). The 20 RulesShort runs of x3 split as follows:

| Cause | Runs | Share of RulesShort | Class |
|---|---|---|---|
| Filter-land chain or Verge condition (H3) | 19, 20, 55, 56, 61, 71, 73, 94, 133 | 9 of 20: about 5.5 pp [3.2, 8.1] | 2, in o4 |
| Sunken Hollow enters tapped on turn 4 (H1) | 0, 12, 106, 109 | 4 of 20: about 2.5 pp [1.0, 5.1] | 2, in o4 |
| No second try after the land drop | 112, 141, 149 | 3 of 20: about 1.8 pp [0.6, 4.4] | 1 |
| Fiery Islet sacrificed in the turn-4 upkeep | 37, 74 | 2 of 20 | 2 |
| Brainstorm in the upkeep taps the blue source | 24, 60 | 2 of 20: about 1.2 pp [0.3, 3.7] | Decision 5 |

Run 20 never tries the cast. Its Gloomlake Verge makes {B} only when you control an Island or a Swamp, and it controls neither, so XMage plays the Verge right. Run 24 could still pay with a Sunken Ruins chain after the upkeep Brainstorm, so it counts under both rows.

1. **Fiery Islet.** Its draw is a rule our engine lacks (Decision 1, class 2). By lift, it costs XMage 4.42 pp on x3 and our engine −1.41 pp on o4: a lift difference of about 5.8 pp. The sample gives about 5.0 pp: 3 of 20 LandShort, 2 of 20 RulesShort and 1 of 20 ColorShort runs. The pilot rule was to remove this cause. It does not, because the pilot now sacrifices the land in the goal turn's upkeep.
2. **No second try after the land drop (class 1).** The shim puts a failed action in `failedThisTurn` (`PilotPlayer.java`), and `ActionList` then leaves it out of the list until the next turn. In runs 112, 141 and 149, XMage offers Obeka before the land drop. The payment fails, because only a filter-land chain could pay. Then the land drop makes a direct payment possible, but the pilot cannot try again. Run 149 shows it: it tries with Command Tower, Graven Cairns, Island and Sol Ring and fails. It then plays Scalding Tarn for Badlands; Island, Badlands, Command Tower and Sol Ring pay {1}{U}{B}{R}. The cast never comes. In run 141 the board and Simian Spirit Guide could pay before the land drop. The matching reads only permanents, so it leaves the spirit guide out, and XMage's own payment then fails. This is option 2(b) above; phase 3E did not apply it. By the sample, it costs about 1.8 pp [0.6, 4.4].
3. **Casts in the upkeep (Decision 5).** Runs 24 and 60: an upkeep Brainstorm taps the only blue source. About 1.2 pp [0.3, 3.7].
4. **Urza's Saga (H6).** 1 of 20 LandShort runs (21) loses the Saga. About 0, as before.

These estimates add to about 8.8 pp (5.8 + 1.8 + 1.2), against a gap of 9.0 pp. o3 is an upper bound on H3, so the part with no named cause is at least about 0.2 pp. The sample intervals are wide, so this part can be a few pp.

### Verdict after phase 3E

Not explained, and not ready to accept. The gap is −9.0 pp [−10.4, −7.7] against o4 and −14.9 pp [−16.2, −13.6] against o0. Most of it is rules our engine lacks (H1, H3, H6 and Fiery Islet's draw) and the upkeep casts. One cause is class 1: the shim does not let the pilot try the cast again after a land drop. By the plan, a class 1 cause is fixed, not accepted. The pilot rule of Task 3 also misses its aim, because the sacrifice moved to the goal turn's upkeep. The user decides both points before the Obeka comparisons go to `accepted.json`. The land-window arm of Obeka was not traced in phase 3E.

### x4: Tasks 6 and 7

x4 runs on image `mtg-archive-xmage:3e` (`xmage-1.4.61V1+beee0dd6`), with 0 engine errors. Task 6 is option 2(b): the shim tries a failed cast again when a new mana source appears, and the maximum matching counts a spirit guide in hand after the board. Task 7 changes the pilot rule: a land does not spend itself to draw on or before the goal turn until the goal holds.

**Tasks 6 and 7 together closed 8.6 pp** [7.2, 9.9] (x4 against x3). No trace measures each task alone.

- LandShort fell from 2413 to 1730. Most of this is Task 7: the Fiery Islet failure rate fell from 69.4% to 22.2%, near o4's 20.9%.
- RulesShort fell from 1232 to 1063 (about 1.7 pp). This fits the 1.8 pp estimate for the second try.
- ColorShort stayed at about the same count (327 to 322).
- Failed cast tries (`cannot_cast`, all runs, all turns) fell from 2402 to 2038.

**The causes left.** x4 against o0 is −6.3 pp [−7.6, −5.1]; x4 against o4 is −0.5 pp [−1.8, 0.8]. o4 gives our engine the land order (H4), Sunken Hollow tapped (H1) and the upper bound of the filter lands and Verges (H3). So these rules, which our engine lacks, cover the gap to o0 within the sample. o3 is an upper bound on H3, so a small cause with no name can still hide in the gap. The other known causes are small: the upkeep casts (Decision 5, about 1.2 pp by the x3 sample), Urza's Saga (H6, about 0) and the rest of the Fiery Islet difference (22.2% against 20.9%). I read no x4 failures, so these sizes come from the x3 sample.

In phase 3E, Task 8, the user accepted both Obeka comparisons (`xmage_lower`, `../accepted.json`) on this evidence.
