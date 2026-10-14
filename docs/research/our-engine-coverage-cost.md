# Cost for our engine to reach 50% and 90% coverage

Research for Cost for our engine to reach 50% and 90% coverage, under the map Simulation engine: build our own or integrate an existing engine. It builds on Mechanics coverage audit: what real Commander decks need. Data date: 2026-09-26. Engine state: `main` on that date.

## Answer

- **About 50% (52.6%, the audit's first 10 units)** costs 41 to 98 capability PRs and 13,000 to 36,000 lines of engine code. That is 2 to 5 times the goldfish engine and DSL of today (6,680 lines). It also needs about 1,000 new card definitions with about 2,270 Oracle clauses: 57 to 162 authoring sessions. A pilot-fix tail follows each capability. The one observed cycle had one fix PR for each capability PR.
- **About 90% (90.0%, the first 23 units)** costs 67 to 164 capability PRs and 19,000 to 53,000 lines of engine code, 3 to 8 times today. It needs about 2,050 definitions with about 5,060 clauses: 127 to 362 sessions.
- **Those card counts hold only for the 52 sample decks.** 39% of the sample's nonbasic slots hold a card that appears only once. On a deck outside the sample, 90% needs about 5,800 authored cards (3,800 to 7,900 by source), and 50% needs about 800. That is 360 to 1,040 sessions for 90%.
- **At the pace this repo has shown** (1.8 simulation PRs per day from 2026-09-11 to 2026-09-25), 50% takes about 2 to 7 months and 90% about 4 to 13 months. 90% for any deck takes 8 months to 2 years. This is an extrapolation, not a measurement.
- **ADR 0002's closed vocabulary survives, but its "no general effect language" line does not.** Forge covers these cards with a closed set of names, so closed names scale. But the names our DSL needs grow from 15 today to about 210 at 50% and about 345 at 90%. The parameter keys grow from 37 to about 340 and about 510. About one card in five needs a computed amount, one in seven a condition, and one in four a target filter. That is a closed expression grammar, and ADR 0002 needs an amendment to allow it.
- **The definition/policy seam survives.** Every new choice goes to `IPilotPolicy`. The pilot needs 12 to 15 new choice kinds by 50% and about 20 by 90%, on top of the 12 it has. Forge needs an `AILogic` hint in 8% to 9% of these cards' scripts; our pilot must value those cards without a hint.
- **50% breaks ADR 0005 and the tier-4 exclusion in `GLOSSARY.md`.** Unit 3 (opponents' objects) and unit 4 (the stack and responses) are tier-4 work. ADR 0005 calls the response window "the single biggest step toward the out-of-scope tier-4 engine".
- **The cheap gains are real and need no subsystem.** Tapped-entry lands and self-damage mana sources are 477 slots: coverage goes from 10.2% to about 21%. The source profile already has the enters-tapped facet. The work is 3 to 5 small PRs and about 17 definitions.

## Question

The ticket asks, for each subsystem in the audit's greedy order: the change to `GameState`, the turn loop, and the effect vocabulary; the pilot choice kinds it adds; the authoring cost per card; and whether it fits the closed vocabulary and the definition/policy seam of ADR 0002. It asks for the total effort to reach 50% and 90%, with a range and its basis, and for the cost of the two cheap gains the audit names.

## Basis: what this repo's history shows

### Engine work

Line counts are insertions under `src/MtgArchive/Simulation/` (engine) and `tests/` (tests), from `git show --numstat`.

| Work | PRs | Engine lines | Test lines | When |
|---|---|---|---|---|
| Whole tier-2 and tier-3 engine: Effect DSL schemas and validators, Goldfish engine core, Effect interpreter port with conformance suite and check-runner CI, triggers, mulligan, pilot, Monte Carlo, endpoint, disruption, paired runs | 10 | 6,972 | 4,698 | Merged 2026-09-11 to 2026-09-14. Design tickets (Effect DSL shape, Goldfish engine design, Tier-3 abstracted disruption design) came first, and the interpreter was a port of a prototype. |
| Triggered abilities: 3 trigger events and trigger ordering | 1 | 289 | 261 | 2026-09-13 |
| Disruption events: schema, scheduler, mechanics, targeting | 1 | 380 | 443 | 2026-09-13 |
| Goldfish engine: fetch-profile lands search instead of running inert: a source-profile facet drives a land | 1 | 49 | 183 | 2026-09-20 |
| MDFC faces: a goal names a face and play MDFC land faces | 2 | 276 | 638 | 2026-09-23 |
| search_library to library_top, and a type list in the filter: richer parameters | 1 | 94 | 125 | 2026-09-25 |
| scry and look_at_top: two primitives | 1 | 284 | 343 | 2026-09-25 |
| activated ability from hand with an exile_self cost: a zone and a cost | 1 | 329 | 436 | 2026-09-25 |
| Pilot fixes after those capabilities: library_top tutor timing, mulligan tutor payability, keep needed colors inside a tier | 3 | 518 | 253 | 2026-09-25 |

What these numbers give:

- **Size classes.** A new parameter or primitive is one PR and 100 to 350 engine lines. A new mechanism (triggers, disruption, MDFC faces) is 2 to 4 PRs and 300 to 1,200 lines. The whole engine is 10 PRs and about 7,000 lines.
- **Test ratio.** Test lines are 0.7 times engine lines for the whole engine and 1.3 times for the three capability PRs.
- **Vocabulary rate.** The three capability PRs added about 6 names or parameter values: `library_top`, a type list, `scry`, `look_at_top`, the `hand` zone, and `exile_self`. That is 2 per PR, and about 120 engine lines per name. Deep subsystems add machinery on top of that rate.
- **Pilot tail.** The seven cards that those three PRs unparked lowered the turn-1 Pithing Needle floor from ≥19.7% to ≥18.5% (Effect DSL: vocabulary for the parked turn-one enablers, floor comment). Three pilot and mulligan fixes followed: Goal-aware mulligan counts a library_top tutor for a turn-1 goal, Goal-aware mulligan keeps a tutor the hand cannot pay for by the deadline, and Goal-aware pilot spends the only color source before a ramp spell that needs it. New card behavior stresses the goal-aware heuristics. This note budgets 0.5 to 1 fix PR per capability PR.
- **Pace.** 27 PRs touched `src/MtgArchive/Simulation/` or `data/staples/` from 2026-09-11 to 2026-09-25: 1.8 per day. Open-to-merge times are minutes, so the wall clock of a PR does not measure effort. Design and human review limit the pace.

### Authoring

| Session | Cards authored | Cards parked | Clauses | Lines (definitions, checks, corpus) |
|---|---|---|---|---|
| Staples: author Dark Ritual, park eleven turn-one enablers | 1 | 11 | 1 | 181 |
| Staples: author the seven turn-1 enablers | 7 | 0 | 14 | 954 |

- The best session so far reviewed 7 cards and 14 clauses. The earlier authoring rule (removed in phase 4) asked for 10 to 20 cards per session. With today's simple cards (about 2 clauses each), that is 20 to 40 clauses. This note uses **14 to 40 clauses per session**.
- The 14 staples average 111 lines of definition and check each. Checks run from 45 to 178 lines.
- The 13 sample staples average 1.38 clauses by this note's count (lines and sentences, reminder text removed). The cards each unit adds average 1.5 to 3.1 clauses, and modal spells 4.3. Authoring cost per card rises 1.6 to 2.3 times.
- Pinned-text drift is a running cost. One Scryfall templating change forced a re-review of 2 of 14 staples within two weeks (Staple errata: re-pin Pithing Needle and Evolving Wilds after Scryfall self-reference templating). At 1,000 staples, one such change can flag hundreds of cards. The rate is not measured.

### Vocabulary yardstick: Forge

Forge's closed vocabulary at `95dc682b` has 202 effect APIs (`ApiType.java`), 148 trigger modes (`TriggerType.java`), 40 replacement events (`ReplacementType.java`), and 89 static modes (`StaticAbilityMode.java`). Its scripts cover 99.67% of the Commander pool (audit).

`forge_vocab.py` reads the Forge script of each sample card (2,444 of 2,447 found; two meld cards and one Universes Within name are missing). It counts the distinct names that the cards of each greedy step use:

| Coverage point | Cards | Effect APIs | Trigger modes | Replacement events | Static modes | Keywords | Cost parts | Names | Parameter keys |
|---|---|---|---|---|---|---|---|---|---|
| Today (10.2%) | 82 | 9 | 0 | 0 | 1 | 1 | 4 | 15 | 37 |
| Step 10 (52.6%) | 1,063 | 87 | 31 | 8 | 15 | 59 | 10 | 210 | 343 |
| Step 23 (90.0%) | 2,104 | 103 | 44 | 12 | 30 | 107 | 47 | 343 | 507 |
| Whole sample | 2,447 | 116 | 47 | 14 | 35 | 122 | 51 | 385 | 518 (step 25) |

The 15 Forge names for today's covered cards match our 15: 8 primitives, 3 trigger events, and 4 costs. Of the cards at step 10, 19% use a computed amount (`Count$`), 13% a condition, 23% a target filter, and 8% an `AILogic` hint. At step 23 the shares are 18%, 14%, 29%, and 9%.

## Method

1. The audit's `classify.py` output (`results.json`) holds each card's missing tags and slots. `cost.py` repeats the audit's greedy order exactly (every cumulative number matches `analysis.txt`) and counts, per step, the unique cards it unlocks and their clauses.
2. A card that a step unlocks needs a definition, except a land whose gaps a source-profile facet can carry (the "facet route"). Today's `expressible` cards (33 cards, 104 slots) count as unauthored.
3. `rank.py` reads the EDHREC rank of each slot's card. If 90% of slots hold a card of rank 5,764 or better, authoring the top 5,764 cards covers about 90% of a deck from the same population. This estimates the cost for decks outside the sample. The Good-Turing estimate (share of slots whose card appears once) says the same thing: 38.8% of a new deck's nonbasic slots hold a card that the sample never saw.
4. Engine size per unit uses the size classes above, by analogy. A second basis checks the total: new vocabulary names at 2 to 4 per PR. A third basis checks the lines: today's engine is 6,680 lines for rules sections of about 7,000 words in the Comprehensive Rules (CR 103, 500–505, 601, 602, 605). That is about 1 line per word. The CR sections of the first 10 units hold about 27,000 words (CR 111, 115, 117, 119, 120, 122, 306, 405, 506–511, 603, 608, 611–616). This ratio is a rough proxy.
5. The facet values come from `source_profile.enters_tapped` in the archive database (`facet_sql.py`).

## Per unit

"Lines" means engine lines under `src/MtgArchive/Simulation/`, without tests. Tests add 0.7 to 1.3 times as many. Choice kinds today: the main-phase action, mana payment, `search_library`, `put_back`, `name_card`, `scry`, `order_top`, `may_shuffle`, `order_triggers`, the cleanup discard, the mulligan keep, and the bottom (12).

### Engine, choices, and fit

| Step | Unit | Coverage | Change to `GameState`, turn loop, and vocabulary | New choice kinds | Fit with ADR 0002 and other decisions | PRs | Lines |
|---|---|---|---|---|---|---|---|
| 1 | Replacement and prevention | 16.7% | A replacement hook in `EnterBattlefield` (tapped, with counters, "as it enters"). Then "would/instead" events for draw, damage, and zone moves (CR 614–616). A `replacement` ability kind with a closed event list. | Pay life or enter tapped; an "as it enters" choice; the order of replacements (CR 616.1) | Fits. Forge has 40 replacement events; the sample uses 14. | 4–8 | 1,000–3,000 |
| 2 | Life total | 21.4% | `GameState` has `Life` and `PayLife`. Add gain, loss, damage to self, a `pay_life` cost, and life events. | None; a pain land's color choice is part of mana payment | Fits | 1–3 | 200–800 |
| 3 | Targets, opponents, and other players' objects | 23.2% | Split `GameState` into players, each with life, zones, and a battlefield. Every zone method, goal predicate, heuristic, and disruption rule changes. Opponents' objects need a source: opponent decks or a scripted board. | Targets (with "up to N"), a player, divided amounts; an opponent policy beside the pilot | Names fit. The card filter grows from 2 keys to a filter grammar. Breaks ADR 0005 and the tier-4 exclusion. | 6–12 | 2,500–6,000 |
| 4 | Stack and responses | 25.7% | Priority in each step (CR 117), a real stack (CR 405), and a target check on resolution (CR 608.2b). Today every spell casts at sorcery speed and resolves at once. | Respond or pass at each priority; what to counter | Names fit (`counter`). Breaks ADR 0005. Pilot calls per game rise far above today's 40.2 (Can Forge accept our goal-aware pilot's choices?). | 5–10 | 1,500–4,000 |
| 5 | Complex mana | 28.1% | `ManaPool` and `ManaSolver`: any color, colors read from other lands, restricted mana, several mana at once. Exotic Orchard and Fellwar Stone read opponents' lands, so they need step 3. | The color of "any color" mana, inside payment | Fits: richer `add_mana` parameters (growth step 1) | 2–4 | 400–1,200 |
| 6 | Trigger events | 30.8% | Emit sites for cast, attack, damage, dies, leaves, another permanent enters, landfall, sacrifice, counters, and life; intervening "if" clauses (CR 603.4). Rhystic Study and Mystic Remora trigger on opponents' spells, so they need step 3 and an opponent that acts. | "You may" yes or no; trigger targets | Fits. Forge has 148 trigger modes; the cards to step 10 use 31. | 3–6 | 600–1,800 |
| 7 | Combat | 35.1% | Creatures with power, toughness, damage, and summoning sickness. A combat phase of five steps (CR 506–511), a second main phase, commander damage, and combat keywords. `GLOSSARY.md` today says "no combat, no second main". | Attackers and whom each attacks; blockers (opponent policy); damage assignment | Names fit (keywords are a closed list). Goal predicates must grow past `on_battlefield`, `cast`, and `all_of`. ADR 0003's pilot has no attack rule. | 8–15 | 3,000–8,000 |
| 8 | Continuous effects (layers) | 42.1% | Characteristics become computed: seven layers, timestamps, dependencies (CR 613), and durations. Equipment and Auras attach. | Equip and attach targets | Names fit (Forge uses one `Continuous` mode). Its parameters are a filter-and-modifier grammar. | 8–15 | 3,000–8,000 |
| 9 | Counters | 47.2% | Counters on permanents; state-based actions (CR 704) such as +1/+1 and -1/-1 counters cancelling and 0 loyalty; planeswalker loyalty abilities. | Proliferate targets; how to divide counters | Fits | 2–4 | 500–1,500 |
| 10 | Tokens | 52.6% | Token objects from a catalog (Treasure, Clue, Food, creature tokens). Tokens leave the game off the battlefield. Many cards here give tokens to opponents (An Offer You Can't Refuse, Beast Within), so they need step 3. | None | Fits | 2–4 | 400–1,200 |
| 11 | Graveyard and exile as play zones | 58.2% | `GameAction` kinds to cast or play from the graveyard and exile; permissions with durations; flashback, escape, and free casts. | Which card to return; casts from other zones in the action list | Fits | 4–8 | 1,200–3,000 |
| 12 | Discard | 62.3% | A discard effect (chosen and random) and cycling. Activation from hand exists since the exile_self PR. | The discard pick at effect time | Fits | 1–2 | 200–600 |
| 13 | Rule restrictions | 66.0% | A static check on actions: "can't", hand size, "doesn't untap". | None | Fits (Forge static modes such as `CantBeCast`) | 2–5 | 500–1,500 |
| 14 | Put onto the battlefield | 68.6% | Split destinations (Cultivate) and "put from your hand". | The split-destination pick | Fits: growth step 1 on `search_library` | 1 | 100–300 |
| 15 | Search with complex filters | 71.2% | Filter keys: mana value, subtype, color, "basic land card". Path to Exile gives an opponent a search, so it needs step 3. | None | Fits; the filter widens | 2–3 | 300–900 |
| 16 | Sacrifice another permanent | 73.9% | Sacrifice as a cost and an effect, with a filter. "Each opponent sacrifices" needs step 3. | What to sacrifice | Fits | 1–2 | 150–500 |
| 17 | Other costs | 76.8% | Pay life, discard, remove counters, tap creatures, sacrifice with a filter. Forge cost parts go from 24 to 40 at this step. | Which objects pay the cost | Fits | 2–4 | 400–1,200 |
| 18 | Draw with a variable count or other players | 79.4% | Amount expressions; other players draw (Windfall). | None | Strains: needs amount expressions | 1–3 | 300–900 |
| 19 | Cost reduction | 81.9% | Cost modifiers in `CostOf`; convoke, affinity, improvise. | Convoke creatures, inside payment | Fits | 2–3 | 400–1,000 |
| 20 | Modal spells | 84.3% | Modes at cast (CR 700.2); spree and escalate. | Modes | Fits (Forge `Charm`) | 1–2 | 200–600 |
| 21 | Alternative and additional costs | 86.6% | Kicker, overload, evoke, buyback, bestow, casualty: each a keyword with cost rules (CR 118.9, 702). | Which cost to pay | Fits; one keyword each | 4–8 | 1,000–3,000 |
| 22 | Mill | 88.5% | A mill effect (CR 701.17). | None | Fits | 1 | 100–300 |
| 23 | Copy effects | 90.0% | Copiable values (CR 707); copy a spell on the stack, so it needs step 4. | What to copy; new targets for a copy | Fits | 4–8 | 1,500–4,000 |
| 24 | X costs | 91.6% | Announce X (CR 107.3, 601.2b). | The value of X | Fits | 1–2 | 200–600 |
| 25 | Untap | 93.0% | An untap effect. | Untap targets | Fits | 1 | 100–200 |

### Authoring load

"Cards" are unique sample cards that the step unlocks. "Facet" is the number of those cards that a source-profile facet can carry. "Clauses" uses this note's count.

| Step | Unit | Slots added | Cards | Facet | Mean clauses | Clauses | Cumulative cards (facet route) |
|---|---|---|---|---|---|---|---|
| 0 | Expressible today, unauthored | 104 | 33 | 0 | 1.33 | 44 | 33 |
| 1 | Replacement and prevention | 287 | 87 | 84 | 2.15 | 187 | 120 (36) |
| 2 | Life total | 209 | 38 | 21 | 2.42 | 92 | 158 (53) |
| 3 | Targets and opponents | 80 | 46 | 0 | 1.72 | 79 | 204 (99) |
| 4 | Stack and responses | 114 | 38 | 0 | 1.79 | 68 | 242 (137) |
| 5 | Complex mana | 105 | 33 | 0 | 2.39 | 79 | 275 (170) |
| 6 | Trigger events | 119 | 50 | 0 | 1.54 | 77 | 325 (220) |
| 7 | Combat | 192 | 156 | 0 | 2.19 | 342 | 481 (376) |
| 8 | Layers | 307 | 200 | 0 | 2.40 | 480 | 681 (576) |
| 9 | Counters | 228 | 164 | 0 | 2.55 | 418 | 845 (740) |
| 10 | Tokens | 241 | 169 | 0 | 2.37 | 400 | 1,014 (909) |
| 11 | Graveyard and exile | 247 | 164 | 0 | 2.66 | 436 | 1,178 |
| 12 | Discard | 182 | 120 | 0 | 2.60 | 312 | 1,298 |
| 13 | Rule restrictions | 163 | 84 | 0 | 2.52 | 212 | 1,382 |
| 14 | Put onto the battlefield | 114 | 53 | 0 | 2.28 | 121 | 1,435 |
| 15 | Search with complex filters | 115 | 50 | 0 | 2.06 | 103 | 1,485 |
| 16 | Sacrifice another permanent | 121 | 77 | 0 | 2.52 | 194 | 1,562 |
| 17 | Other costs | 130 | 100 | 0 | 2.59 | 259 | 1,662 |
| 18 | Variable draw | 114 | 75 | 0 | 2.36 | 177 | 1,737 |
| 19 | Cost reduction | 113 | 70 | 0 | 2.60 | 182 | 1,807 |
| 20 | Modal spells | 105 | 69 | 0 | 4.32 | 298 | 1,876 |
| 21 | Alternative costs | 103 | 70 | 0 | 2.91 | 204 | 1,946 |
| 22 | Mill | 83 | 53 | 0 | 2.72 | 144 | 1,999 |
| 23 | Copy effects | 67 | 56 | 0 | 2.79 | 156 | 2,055 (1,950) |

Most unlocked cards are rare in the sample: from step 7 on, 58% to 84% of each step's cards fill exactly one slot. Each definition buys little coverage by itself.

## Totals

| | ~50% (step 10, 52.6%) | ~90% (step 23, 90.0%) | ~90% for any deck |
|---|---|---|---|
| Capability PRs, by unit | 41–81 | 67–131 | as 90% |
| Capability PRs, by new names at 2–4 per PR | 49–98 (195 new names) | 82–164 (328 new names) | as 90% |
| Capability PRs, range used | 41–98 | 67–164 | 67–164 |
| Pilot-fix PRs (0.5–1 per capability PR) | 20–98 | 34–164 | 34–164 |
| Engine lines | 13,100–35,500 (CR proxy: about 27,000) | 19,450–53,300 | as 90% |
| Test lines (0.7–1.3 times) | 9,000–46,000 | 14,000–69,000 | as 90% |
| Vocabulary names (today 15) | about 210 | about 345 | about 385 (whole sample) |
| New pilot choice kinds (today 12) | 12–15 | about 20 | about 20 |
| Cards to author | 1,014 (909 facet route) | 2,055 (1,950) | about 5,800 (rank basis; 3,800–7,900) |
| Clauses to author and review | about 2,270 | about 5,060 | about 14,500 (at 2.5 per card) |
| Authoring sessions (14–40 clauses each) | 57–162 | 127–362 | 360–1,040 |
| Calendar at 1.8 PRs per day (extrapolation) | 2–7 months | 4–13 months | 8 months–2 years |

The calendar row adds capability, pilot-fix, and session PRs, then divides by 1.8. It assumes that human review and design keep that pace. Nothing here measures that.

## Does ADR 0002 survive?

- **Closed names: yes.** Forge is the proof. Its closed sets (202 effect APIs, 148 trigger modes, 40 replacement events, 89 static modes) script 99.67% of the pool. The sample's cards need 385 of those names. A hard failure on an unknown name still works at that size. The growth order (parameters, then composition, then a primitive) also still works; most units are parameters and primitives.
- **"Rather than a general effect language": no.** At 50%, one card in five needs a computed amount, such as "X, where X is the number of lands you control". One in seven needs a condition, and one in four a target filter. Forge carries these in a parameter grammar with 343 keys at step 10 (`Count$`, `ValidTgts$`, `Condition` keys). Our filter has 2 keys today. The DSL needs a typed expression grammar for amounts, object filters, conditions, and durations. It can be a closed grammar, but it is a language. ADR 0002 needs an amendment, not a reversal.
- **Ability kinds: they grow.** The five kinds need at least `replacement` and a loyalty form. `static` must carry continuous effects under the layer rules.
- **The definition/policy seam: yes, with pressure.** Every new choice (targets, modes, X, attackers, responses, replacement order) is a pause that `IPilotPolicy` resolves. Definitions stay free of choices. The pressure is on the AI-hint ban. Forge needs `AILogic` in 8% to 9% of these cards' scripts, where its generic AI could not value a card from structure. Our pilot must value those cards from structure and the goal alone. For tier-2 goals ("this card on the battlefield by turn N") most of those choices do not matter. With opponents and combat, they do.
- **Checks and review: they survive but cost more.** A clause map still covers every clause. Cards average 1.6 to 2.3 times today's clauses, and modal spells 3 times.
- **Other decisions do not survive 50%.** Steps 3 and 4 need opponents' objects and a response window. ADR 0005 rules out the window, and `GLOSSARY.md` rules out tier 4. The map already allows the ADR to overturn both.

## Cheap gains

These gains need no rules subsystem. They make up the audit's lenient view: 477 slots, from 10.2% to about 21%. (The lenient view counts 478; the extra slot is Electrolyze, which is not a mana source.)

| Group | Cards | Slots | Facet today | Engine work | Definitions |
|---|---|---|---|---|---|
| Lands that always enter tapped (tri-lands, guildgates, temples, bounce lands) | 40 | 93 | `tapped` | Read the facet in `PlayLand`. One small PR, like the fetch-facet PR (49 engine lines, 183 test lines). | 10 temples need an `on_enter` scry definition, which today's vocabulary can express. 7 bounce lands (Golgari Rot Farm and its cycle) also need a "return a land you control to its owner's hand" effect. The audit tags them as one mechanic away; their Oracle text shows a second gap. |
| Check, slow, fast, and reveal lands | 44 | 190 | `conditional` | The facet does not record the condition. Extend the source-profile parser with a closed condition family: "unless you control a [type] or a [type]", "unless you control two or more other lands", "two or fewer other lands", "reveal a [type] card from your hand". Then evaluate it on the land drop. 1 to 2 PRs. | None |
| Shock lands | 10 | 95 | `untapped` (a free choice) | A choice point "pay 2 life or enter tapped". `GameState.PayLife` exists. One small PR. | None |
| Pain lands and Ancient Tomb | 11 | 55 | `untapped` | Record the self-damage per produced color in the source profile ("deals N damage to you"). Let a payment charge the life. The solver prefers the painless color. 1 to 2 PRs. | None |
| Nine Talismans and Elves of Deep Shadow | 10 | 44 | none (nonland) | The same self-damage record, for nonland mana producers. | None |

The enters-tapped facet already exists (`GLOSSARY.md`, Enters-tapped state; Enters-tapped facet for lands, derived with source profiles). Today only search uses it. The total cost is 3 to 5 small PRs, about 400 to 1,200 engine lines, and about 17 definitions. The groups sum to 105 lands with 433 slots, plus 10 nonland sources with 44 slots: 477 slots.

In a goldfish, self-damage matters only for the loss at 0 life and for affordability. Tapped entry changes the turn a land can pay, so it moves real floors.

Two more near-cheap gains are one unit away: split destinations for Cultivate and Kodama's Reach (40 slots, growth step 1 on `search_library`) and "any color" mana for Commander's Sphere and Lotus Petal (28 slots, a richer `add_mana`). Also, the 33 cards that today's vocabulary can express (104 slots) still need 2 to 5 authoring sessions. The audit counts them as covered already.

## Limits and unverified points

- **The engine ranges are judgment.** They come from analogy with this repo's PRs, checked by a vocabulary-count basis and a CR-word proxy. The three bases agree within the ranges, but no unit here was built.
- **The calendar row is an extrapolation.** The observed pace came from bursts after design tickets were settled, and the core engine port reused a prototype. Subsystems such as layers and the stack have no design yet.
- **Human review time is not measured.** Sessions and clauses are the unit here, not hours.
- **Throughput is not measured.** Opponents, priority passes, and layer recomputation all add work per game. The map's bar (10,000 runs in under 2 minutes) must be re-measured for each step past unit 2.
- **The clause count is a proxy.** It splits lines and sentences and removes reminder text. The real clause map keeps reminder text, so real counts are higher (Opt: 3 clauses on its sheet, 1 here).
- **Tags come from the audit.** Its patterns can miss or add a tag. This note found one gap: bounce lands (7 cards).
- **"Coverage" means the card plays correctly.** It does not mean the card matters to a goal. Many cards from steps 7 to 10 (creatures, removal) matter little to a goldfish goal.
- **The any-deck figure assumes that other decks draw cards the way the sample does.** The sample leans toward popular lists.
- **Forge is a yardstick, not a model.** Forge names count the vocabulary our DSL would need if it cut the rules the way Forge does. Our cut can differ.
- **The option to import Forge scripts can change the authoring cost.** Forge script import: mapping share and GPL status is still open.

## Reproduce

The scripts are in `docs/research/our-engine-coverage-cost/`. They read the audit's `results.json` from `docs/research/mechanics-coverage-audit/` on branch `research/mechanics-coverage-audit`; run the audit's `classify.py` first to make it.

1. `python cost.py <audit_dir> > cost.txt`: the greedy order, cards, clauses, facet route, frequency tail, and cheap gains.
2. `python rank.py <audit_dir> > rank.txt`: the EDHREC rank cutoffs.
3. `python forge_fetch.py <audit_dir>`: fetches the sample's Forge scripts at `95dc682b` into `forge_scripts/` (gitignored; Forge is GPL-3.0). Needs the `gh` CLI.
4. `python forge_vocab.py <audit_dir> > forge-vocab.txt`, then `python forge_params.py >> forge-vocab.txt`: Forge names and parameter keys per step.
5. `python facet_sql.py <audit_dir>`, then pipe `facet.sql` into `psql` on the `mtg-archive` compose stack's Postgres: `facets.txt`.

## Sources

- This repo's commit history on 2026-09-26 for `src/MtgArchive/Simulation/`, `tests/`, and `data/staples/`, and its PR dates.
- `src/MtgArchive/Simulation/Dsl/EffectVocabulary.cs`: 8 primitives, 3 trigger events, 4 costs, 2 filter keys, 5 ability kinds.
- `src/MtgArchive/Simulation/Goldfish/ChoicePoint.cs`, `IPilotPolicy.cs`, `EffectInterpreter.cs`, `Triggers.cs`: today's choice kinds.
- `src/MtgArchive/Simulation/Goldfish/GameState.cs` and `GoldfishRun.cs`: the single-player state and the turn loop.
- `docs/adr/0002-effect-dsl-closed-vocabulary.md`, `0003-goal-aware-greedy-pilot-floor-answers.md`, `0005-abstracted-disruption-no-response-window.md`; `GLOSSARY.md`; `.claude/rules/simulation.md`.
- Closed issues: Effect DSL: vocabulary for the parked turn-one enablers and its floor comment; Goal-aware mulligan counts a library_top tutor for a turn-1 goal; Goal-aware mulligan keeps a tutor the hand cannot pay for by the deadline; Goal-aware pilot spends the only color source before a ramp spell that needs it; Staple errata: re-pin Pithing Needle and Evolving Wilds after Scryfall self-reference templating; Can Forge accept our goal-aware pilot's choices? (40.2 pilot calls per goldfish).
- The audit on branch `research/mechanics-coverage-audit`: `docs/research/mechanics-coverage-audit.md`, `analysis.txt`, `card-coverage.csv`, `classify.py`.
- Archive database: `source_profile.enters_tapped`; `cr_rule_chunk` for CR effective 2026-09-25 (word counts per section).
- Card-Forge/forge at `95dc682b`: `forge-game/src/main/java/forge/game/ability/ApiType.java`, `trigger/TriggerType.java`, `replacement/ReplacementType.java`, `staticability/StaticAbilityMode.java`, and `forge-gui/res/cardsfolder/` scripts for the sample cards.
