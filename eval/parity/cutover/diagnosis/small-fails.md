# Small parity failures: diagnosis

The phase 3C parity gate failed four comparisons with small gaps (`../report.md`). This diagnosis names the cause of each. Each trace is 10,000 runs at base seed 20260928. Each interval is the 95% Newcombe interval of XMage minus our engine.

| Comparison | Ours | XMage | Difference | 95% interval | Cause | Class |
|---|---|---|---|---|---|---|
| dq2-needle-t1/goal-aware | 2162 (21.6%) | 2022 (20.2%) | −1.4 pp | [−2.5, −0.3] | Lands that enter tapped (H1) | 2 |
| dq2-demonic-t1/goal-aware | 197 (2.0%) | 153 (1.5%) | −0.4 pp | [−0.8, −0.1] | Lands that enter tapped (H1) | 2 |
| dq1-sol-ring-t2/goal-aware | 2910 (29.1%) | 3247 (32.5%) | +3.4 pp | [2.1, 4.6] | Land order (H4) | none of 1 and 2 |
| dq1-sol-ring-t2/land-window | 1151 (11.5%) | 1324 (13.2%) | +1.7 pp | [0.8, 2.6] | Land order (H4) | none of 1 and 2 |
| dq1-demonic-t2/goal-aware (Task 8) | 2566 (25.7%) | 2690 (26.9%) | +1.2 pp | [0.0, 2.5] | Land order (H4) less three land rules; the retry fix adds 10 runs | none of 1 and 2, and 2 |

Each ours-trace gives the successes of its `../baseline.json` entry, so the pilot rule of Task 3 does not change our engine's play. The XMage traces use image `mtg-archive-xmage:3e` (`xmage-1.4.61V1+96331334`), which has the payment fix of Task 2. Each XMage trace gives the same floor as phase 3C, so the payment fix does not change these four comparisons.

The trace files are `<case>-o.json` (our engine) and `<case>-x.json` (XMage), each with a `.failures.txt`. The ablation traces change only our engine:

- `-et` makes Dragonskull Summit, Drowned Catacomb, Sulfur Falls and Sunken Hollow enter tapped (`--enters-tapped`).
- `-name` plays lands in name order, as XMage does (`--land-order name`).
- `-et-name` does both.

| Ablation trace | Ours | XMage | XMage minus ours |
|---|---|---|---|
| needle-t1-o-et | 1891 (18.9%) | 2022 | +1.3 pp [0.2, 2.4] |
| needle-t1-o-name | 2225 (22.3%) | 2022 | −2.0 pp [−3.2, −0.9] |
| needle-t1-o-et-name | 1983 (19.8%) | 2022 | +0.4 pp [−0.7, 1.5] |
| demonic-t1-o-et | 153 (1.5%) | 153 | +0.0 pp [−0.3, 0.3] |
| demonic-t1-o-name | 220 (2.2%) | 153 | −0.7 pp [−1.0, −0.3] |
| demonic-t1-o-et-name | 171 (1.7%) | 153 | −0.2 pp [−0.5, 0.2] |
| sol-ring-t2-o-name | 3331 (33.3%) | 3247 | −0.8 pp [−2.1, 0.5] |
| sol-ring-t2-lw-o-name | 1321 (13.2%) | 1324 | +0.0 pp [−0.9, 1.0] |

On turn 1, the `-et` ablation is exact, not a bound. A checkland enters tapped unless you control a land of its basic types, and Sunken Hollow enters tapped unless you control two basic lands. On turn 1 you control no land, so each of the four always enters tapped on XMage. The failure classes of the trace (LandShort, ColorShort, RulesShort) fit a cast goal with colored pips. They do not fit these goals, so this diagnosis does not use them.

## dq2-needle-t1: lands that enter tapped

**Top path differences** (ours, XMage):

- `T1 land > T1 cast Pithing Needle`: 1378, 1191 (−187).
- `T1 land > T1 activate Bloodstained Mire > T1 cast Pithing Needle`: 51, 134 (+83). `… activate Arid Mesa …`: 49, 132 (+83).
- `T1 land > T1 activate Verdant Catacombs > T1 cast Pithing Needle`: 53, 8 (−45). `… activate Wooded Foothills …`: 41 on ours; not in XMage's top 20, so 8 or fewer.

The fetch-land rows show the land order: XMage offers lands in name order, so it plays Arid Mesa and Bloodstained Mire first. The largest loss is on the plain path, a land and then the Needle.

**Runs read** (`needle-t1-x.failures.txt`, 40 runs). Two runs fail only on XMage:

- Run 19 keeps Drowned Catacomb, Steam Vents, Pithing Needle and Sol Ring. The pilot plays Drowned Catacomb first by name, and it enters tapped. The Needle stays in hand. On our engine, Drowned Catacomb enters untapped and pays for the Needle.
- Run 8 keeps Drowned Catacomb, Dark Ritual and Demonic Tutor. Drowned Catacomb enters tapped, so the ritual has no mana. On our engine, the ritual pays for Demonic Tutor, which finds the Needle.

The other 38 runs fail on both engines. All 20 LandShort runs end turn 1 with no land that makes mana. 16 of them keep a hand with no land after three mulligans and draw none (runs 115, 125, 206 and others). 4 play Windswept Heath, which finds no land in this deck (runs 105, 317, 375, 1022). The other 18 RulesShort runs never hold the Needle, or a tutor with the mana for it.

**Cause.** On turn 1, XMage enters Drowned Catacomb, Dragonskull Summit, Sulfur Falls and Sunken Hollow tapped. Our engine has no enters-tapped rule. The land order adds to it: name order puts Drowned Catacomb before most untapped lands. With both ablations, our engine comes to 19.8%, and the gap is +0.4 pp [−0.7, 1.5]. The enters-tapped rule alone moves our engine 2.7 pp below its own floor; name order alone moves it 0.6 pp above. So the enters-tapped rule is the main cause, and name order gives part of it back.

**Class 2: our engine lacks a rule** (H1, Decision 1).

## dq2-demonic-t1: lands that enter tapped

**Top path differences** (ours, XMage). No path differs by more than 50 runs; the goal has only 197 and 153 successes.

- `T1 land > T1 cast Dark Ritual > T1 cast Demonic Tutor > T1 cast Metallic Sliver`: 29, 13 (−16).
- `T1 land > T1 cast Demonic Tutor`: 61, 52 (−9).

**Runs read** (`demonic-t1-x.failures.txt`, 60 runs). Two runs fail only on XMage:

- Run 8 is the same deal as run 8 of needle-t1: Drowned Catacomb enters tapped, and Dark Ritual and Demonic Tutor stay in hand.
- Run 19 keeps Mountain, Wooded Foothills, Demonic Tutor and Simian Spirit Guide. The pilot plays Mountain first by name. Wooded Foothills finds Badlands, which with the spirit guide pays for Demonic Tutor. This run is a land-order loss.

Runs 10 and 17 also play a checkland tapped (Drowned Catacomb, Dragonskull Summit), but they hold no ritual, so they fail on both engines. Most other runs cast Sol Ring on turn 1 with no Demonic Tutor in hand; they fail on both engines.

**Cause.** The enters-tapped ablation alone gives our engine exactly XMage's 153 successes: +0.0 pp [−0.3, 0.3]. With name order too, the gap is −0.2 pp [−0.5, 0.2].

**Class 2: our engine lacks a rule** (H1, Decision 1).

## dq1-sol-ring-t2: land order (both mulligan policies)

**Top path differences, goal-aware** (ours, XMage):

- `T1 land > T1 cast Sol Ring > T2 land > T2 cast Gilded Sentinel`: 353, 285 (−68).
- `T1 land > T1 activate Arid Mesa > T1 cast Vampiric Tutor > T2 cast Sol Ring > …`: 16 or fewer on ours, 44 on XMage. `… activate Arid Mesa > T1 cast Sol Ring …`: 43. `… activate Bloodstained Mire > T1 cast Vampiric Tutor …`: 42.
- `T1 land > T2 cast Sol Ring > T2 land > T2 cast Sliver Construct`: 16 or fewer on ours, 38 on XMage.

XMage gains on the paths that start with a fetch land early in name order (Arid Mesa, Bloodstained Mire) and then use Vampiric Tutor. Vampiric Tutor needs black on turn 1.

**Runs read** (`sol-ring-t2-o.failures.txt`, 20 runs). Two runs fail only on our engine:

- Run 9 keeps Sacred Foundry, Volcanic Island, Command Tower and Vampiric Tutor. Our engine plays lands in draw order, so it plays Sacred Foundry, which makes no black. Vampiric Tutor stays in hand. XMage plays Command Tower first by name; it makes black for Vampiric Tutor.
- Run 12 keeps Temple Garden, City of Brass, Polluted Delta, Badlands and Vampiric Tutor. Our engine plays Temple Garden, which makes no black. XMage plays Badlands first by name.

The land-window arm shows the same paths (`sol-ring-t2-lw-o.json`, `sol-ring-t2-lw-x.json`).

**Cause.** The engines offer the land drops in a different order, and the pilot plays the first land in the list. Our engine lists lands in draw order; XMage lists them by name. In this deck, name order puts lands that make black (Badlands, Bayou, Blood Crypt, City of Brass, Command Tower) and the fetch lands that find them (Arid Mesa, Bloodstained Mire) early. With name order, our engine comes to 33.3% (goal-aware, −0.8 pp [−2.1, 0.5]) and 13.2% (land-window, +0.0 pp [−0.9, 1.0]).

**Class: neither 1 nor 2.** The cause is named, so it is not class 3. XMage does not play a rule wrong, and it offers the pilot no worse view: its order helps the pilot here. Our engine lacks no card rule; the difference is the order of the action list. The Obeka diagnosis calls this H4, and its recommendation 6 leaves it to the user. This comparison is not a candidate for `accepted.json` until the user rules on the land order.

## dq1-demonic-t2: the retry fix moves the interval edge; land order and three land rules make the gap

Phase 3C passed this comparison at +1.1 pp [−0.1, 2.4]. The phase 3E gate fails it at +1.2 pp [0.0, 2.5], and at [0.1, 2.6] on the fresh seed. XMage is higher. This section is Task 8 of the phase 3E plan. The traces are `demonic-t2-*.json` and `.failures.txt`.

| Trace | Engine and change | Floor | XMage minus this trace |
|---|---|---|---|
| demonic-t2-o | ours | 2566 (25.7%) [24.8, 26.5] | – |
| demonic-t2-x3c | XMage, phase 3C shim (`xmage-1.4.61V1+34746b9c`) | 2680 (26.8%) [25.9, 27.7] | +1.1 pp [−0.1, 2.4] against o |
| demonic-t2-x | XMage, phase 3E shim (`xmage-1.4.61V1+beee0dd6`) | 2690 (26.9%) [26.0, 27.8] | +1.2 pp [0.0, 2.5] against o |
| demonic-t2-o-name | ours, name order (`--land-order name`) | 3013 (30.1%) [29.2, 31.0] | −3.2 pp [−4.5, −2.0] (x against o-name) |

demonic-t2-o gives the successes of its `../baseline.json` entry (2566). demonic-t2-x3c gives the XMage floor of the phase 3C gate (26.8%), with the current pilot. So the pilot rule of Tasks 3 and 7 changes neither engine's play on this case.

**What moved the comparison.** The shim change of Task 6 adds 10 successes; nothing else changed. A trace of each shim that keeps all failures (`FailuresKept` set to 100,000 for the run only; these two traces are not committed) gives the same failing runs, less 10 runs that fail on the phase 3C shim only: 584, 3116, 3284, 3408, 3477, 4176, 6041, 6630, 7210 and 8285. No run goes the other way. All 10 runs play Interplanar Beacon and Sol Ring on turn 1. On turn 2 the pilot tries Demonic Tutor before the land drop, and the payment fails, because Interplanar Beacon makes colored mana only for planeswalker spells. The pilot then plays a land that makes black (Misty Rainforest for Bayou, Marsh Flats for Badlands, Underground Sea, Mana Confluence, Reflecting Pool). On the phase 3C shim the pilot cannot try again that turn. On the phase 3E shim it tries again after the new mana, and the cast succeeds. This is the class 1 cause "no second try after the land drop" of `obeka-t4.md`, now fixed. The fix brings XMage nearer to correct play. It moves the lower bound from −0.1 pp to 0.0 pp, 10 runs in 10,000. It is not random noise: both shims are deterministic on the same seeds.

**Why XMage is higher at all.** The +1.1 pp was there in phase 3C. It is the sum of two larger effects in opposite directions.

- Land order (H4) lifts XMage. Name order alone takes our engine from 25.7% to 30.1% (+4.5 pp). In this deck, name order puts black sources (Badlands, Bayou, Blood Crypt, City of Brass, Command Tower) and the fetch lands that find them (Arid Mesa, Bloodstained Mire) early. This is the same cause as `dq1-sol-ring-t2`, on the same deck.
- Three land rules lower XMage. With name order on both engines, XMage is 3.2 pp lower. The lift rows name three lands (failure rate with the land on the board by turn 2):

| Land | o-name | x | Rule on XMage (Oracle text) | Our engine's row |
|---|---|---|---|---|
| Interplanar Beacon | 68.0% of 749 | 80.9% of 723 | Its colored mana can pay only for planeswalker spells | Taps for any color |
| Reflecting Pool | 64.6% of 491 | 79.5% of 580 | Adds only a type that a land you control could produce | Taps for any color but {C} |
| Field of the Dead | 75.9% of 726 | 93.4% of 741 | Enters tapped | Enters untapped |

At these run counts, the three differences come to about 310 runs, or 3.1 pp. This is near the 3.2 pp gap, but the three can overlap in one run, so it is an estimate, not an ablation. Our engine has no ablation for these three lands.

**Top path differences** (o, o-name, x):

- `T1 land > T1 cast Vampiric Tutor > T2 land > T2 cast Demonic Tutor`: 485, 644, 510 (x against o-name −134).
- `T1 land > T1 cast Metallic Sliver > T2 land > T2 cast Demonic Tutor`: 225, 265, 182 (−83).
- `T1 land > T1 activate Arid Mesa > T1 cast Vampiric Tutor > …`: 0, 124, 104. `… activate Bloodstained Mire > T1 cast Vampiric Tutor > …`: 0, 78, 79. `… activate Arid Mesa > T2 land > T2 cast Demonic Tutor`: 0, 69, 74.

The fetch-land paths show the land order: our engine in draw order has none of them in its top 20, and name order gives them at about XMage's counts. The paths with no fetch land lose most on XMage against o-name. These are the paths that use the first land's mana on turn 1 or 2, where the three land rules apply.

**Runs read** (`demonic-t2-x.failures.txt` and the keep-all trace of the phase 3E shim):

- Runs 670 and 1372 play Interplanar Beacon on turn 1 and a land with no black on turn 2 (Stomping Ground, Sacred Foundry). The pilot cannot cast Demonic Tutor. Our engine taps Interplanar Beacon for black and casts it.
- Runs 1934 and 2525 play Reflecting Pool on turn 1 and Steam Vents or Tundra on turn 2. Reflecting Pool can make only blue or red, or blue or white. Our engine taps it for black.
- Runs 398, 408 and 777 play Field of the Dead on turn 1, and it ends turn 1 tapped. Field of the Dead makes only {C}, so this does not change turn 1 in these runs; the turn-2 land makes no black. Its share of the lift difference is not isolated.
- Runs 584, 3116 and 3284 on the phase 3C shim: see "What moved the comparison" above.

**Cause.** The comparison fails because the Task 6 shim fix (a second try after the land drop) adds 10 successes, 0.1 pp. That fix removes a class 1 defect. The gap under it is land order (H4, which the user accepts for `dq1-sol-ring-t2`), less three land rules that our engine lacks: Interplanar Beacon, Reflecting Pool and Field of the Dead.

**Class: land order is neither 1 nor 2; the three land rules are class 2.** No class 1 cause is left. The user accepted it on 2026-09-30 (direction `xmage_higher`, `../accepted.json`), the same ruling as `dq1-sol-ring-t2`.
