# Mechanics coverage audit: what real Commander decks need

Research for Mechanics coverage audit: what real Commander decks need, under the map Simulation engine: build our own or integrate Forge. Data date: 2026-09-26.

## Answer

- **Our engine plays 10.2% of real nonbasic deck slots correctly.** That is 451 of 4,432 slots. The 14 authored staples give 2.8%. Inert mana sources give 2.8%, and fetch lands with a fetch facet give 2.3%. Cards that the current vocabulary can express but nobody has authored give 2.3%. For nonland cards alone, coverage is 6.2%.
- **Forge has a real script for 100% of the sample:** 2,453 of 2,453 unique cards and no blank stubs. Over the whole paper Commander pool, Forge covers 30,749 of 30,851 cards (99.67%). The 102 gaps are stickers, Un-cards, and Conspiracy draft cards.
- **The top missing mechanics, by share of nonbasic slots that need them:** targets and other players' objects 39.1%, combat 36.6%, trigger events outside our three 25.4%, life total 24.2%, replacement effects 19.6%, continuous effects (layers) 17.8%, graveyard and exile zones 13.7%, counters 12.4%, stack and responses 11.6%, and tokens 10.9%.
- **The gap is wide, not deep.** 82% of uncovered slots need two or more missing mechanics. The median is three. A greedy order of additions needs 10 subsystems to reach 52.6% and 25 to reach 93.0%.
- **Two cheap gains exist.** Enters-tapped lands are 256 slots (5.8%) away from coverage by one mechanic. Self-damage mana sources (talismans, pain lands) are 100 slots (2.3%) away.

## Question

Which game mechanics do real Commander decks need, and how much of that does each option cover? The ticket asks for weights by frequency in a deck sample, coverage for our engine and for Forge, and a ranked list of what our engine lacks.

## Sample

52 decks, 5,200 card slots, 2,453 unique cards. 768 slots are basic lands. The coverage numbers use the 4,432 nonbasic slots, the same rule `DslCoverage` uses.

| Source | Decks | Slots | How chosen |
|---|---|---|---|
| User's collection | 1 | 100 | The maintainer's real Golos deck: the rows of `eval/acceptance/done-query-2/collection.csv` without the generator's `Added` stamp. It is also the done-query 1 fixture, so it counts once. |
| Done-query decks | 1 | 100 | The final Obeka list from the verdict comment on Done-query 2 acceptance: turn-1 Pithing Needle from collection (`mechanics-coverage-audit/obeka.txt`). |
| EDHREC average decks | 25 | 2,500 | The top 25 commanders of the "Past 2 Years" list at `json.edhrec.com/pages/commanders/year.json`, then each average deck. Fetched 2026-09-26. |
| Archidekt public decks | 25 | 2,500 | The 25 most-viewed public Commander decks with exactly 100 cards, updated on or after 2025-01-01, not theorycrafted. Maybeboard and other excluded categories are dropped. Fetched 2026-09-26. |

The archive's full collection is not usable as deck evidence. The maintainer has no full ManaBox export, and the imported collection is synthetic: `generate_collection.py` samples it by EDHREC rank. Only the Golos deck is real user data.

The eval set in `eval/retrieval/` holds search queries, not decks, so it adds no decks. `sample.json` holds every decklist with its source id.

## Method

1. **Card text.** Each slot resolves to the archive's card row: Archidekt by `oracle_id`, the rest by exact name or front-face name. All 5,200 slots resolved. The text is the archive's Scryfall snapshot (`updated_at` 2026-09-25 09:01 UTC).
2. **Mechanic tags.** `classify.py` tags each card from its Oracle text, keywords, and type line. It removes reminder text and ability words first. Each tag belongs to a rules subsystem or is an effect kind. The tag list and its patterns are in `classify.py`.
3. **Our engine.** A card counts as covered in one of four classes:
   - `defined`: it has a staple definition (14 cards).
   - `inert_exact`: its only text is one `{T}: Add` mana ability, so the inert model plays it exactly. The inert model knows cost, type, and mana production (`GLOSSARY.md`, Inert card).
   - `fetch_facet`: a land with a fetch activation facet. The engine builds its one ability from the source profile.
   - `expressible`: every clause matches a whitelist of what the 8 primitives, the 4 costs, and the 3 trigger events in `EffectVocabulary.cs` can express.

   Any other card is `uncovered`, and its tags are the mechanics it lacks. The whitelist is strict. For example, "You lose 2 life" is a missing life mechanic even though the Vampiric Tutor staple marks that clause `not_modeled`.
4. **Forge.** Forge HEAD `95dc682b` (2026-09-26), `forge-gui/res/cardsfolder/`, 33,980 scripts. A card is present when a script has its name, front-face name, or Universes Within flavor name. A blank stub is a script with Oracle text and no `A:`, `T:`, `S:`, `R:`, `K:`, or `SVar:` line. The Forge note says an unsupported card loads as a blank card, so a missing script and a stub count the same.
5. **Weights.** A mechanic's weight is the number of nonbasic slots whose card needs it. A card in 20 decks weighs 20.
6. **Check.** A seeded sample of 30 unique cards (seed 207) was read by hand. No card had the wrong coverage verdict. Two tags were wrong and are fixed: ability-word triggers ("Landfall — Whenever") were missed, and instant-speed removal was not counted as a stack need. Some small over-tags remain. For example, "can't be regenerated" counts as a rule restriction, and Deadly Dispute's sacrifice cost also counts as removal.

## Results

### Coverage

| Measure | Our engine | Forge |
|---|---|---|
| Nonbasic slots (4,432) | 10.2% | 100% |
| Nonbasic lands (1,170 slots) | 21.4% | 100% |
| Nonland cards (3,262 slots) | 6.2% | 100% |
| All slots with basics (5,200) | 23.4% | 100% |
| Unique nonbasic cards (2,447) | 82 cards (3.4%) | 2,447 |

Our engine by class, share of nonbasic slots: `defined` 2.8%, `inert_exact` 2.8%, `fetch_facet` 2.3%, `expressible` 2.3%, `uncovered` 89.8%.

| Source | Our engine | Of which `defined` | Forge |
|---|---|---|---|
| Golos deck (collection, done-query 1) | 26.0% | 3.0% | 100% |
| Obeka deck (done-query 2) | 33.0% | 11.3% | 100% |
| EDHREC average decks | 10.5% | 3.3% | 100% |
| Archidekt public decks | 8.1% | 1.9% | 100% |

The two repo decks score higher because their staples were authored for them and they hold many fetch lands and original dual lands.

A lenient view ignores tapped entry and self-damage on mana sources, which the engine now skips. It adds 478 slots and gives 21.0%.

Forge detail:
- The only name mismatch is Baldin, Century Herdmaster. Forge scripts it as E. Honda, Sumo Champion with a Universes Within flavor name.
- Six sampled scripts carry a `# TODO` comment: Demonic Tutor, Mystical Tutor, Indulgent Aristocrat, Goblin Recruiter, Tergrid, and Culling the Weak. All six TODOs are about AI choices, not rules. Our pilot would make those choices.
- `forge_pool.py` checks the whole pool. The 102 missing cards are stickers, Un-cards, and Conspiracy draft cards such as Backup Plan and Power Play. This agrees with the 99.8% claim in `docs/Missing-Cards-in-Forge.md`.

### Needs by rules subsystem

Share of the 4,432 nonbasic slots whose card needs the subsystem. Our engine has none of these.

| Subsystem | Slots | Share | Unique cards | Main tags |
|---|---|---|---|---|
| Targets, opponents, and other players' objects | 1,733 | 39.1% | 1,092 | targets 23.0%, "each player" or opponent text 17.8%, removal 9.0%, board wipes 3.6% |
| Combat | 1,621 | 36.6% | 1,235 | creature bodies 22.6%, combat keywords 20.6%, attack or block text 11.4%, Equipment and Auras 3.3% |
| Trigger events beyond `on_enter`, `at_step`, `on_draw` | 1,127 | 25.4% | 818 | attack or damage triggers 7.2%, dies 4.0%, cast 4.0%, other enters 3.0% |
| Life total | 1,074 | 24.2% | 647 | damage 10.9%, life gain 6.1%, life loss 5.8%, pay life 4.0% |
| Replacement and prevention effects | 870 | 19.6% | 405 | enters tapped 14.3%, enters with counters or a choice 5.4%, "instead" or "would" 2.9% |
| Continuous effects (layers) | 790 | 17.8% | 583 | granted abilities 9.7%, P/T changes 5.8%, protection 5.3%, type changes 3.5%, control change 0.5% |
| Graveyard and exile as play zones | 605 | 13.7% | 423 | graveyard use 10.4%, return from graveyard 5.6%, free casts 3.0%, exile zones 2.7% |
| Counters | 551 | 12.4% | 427 | other counters 10.9%, +1/+1 counters 8.1%, planeswalker loyalty 1.3% |
| Stack and responses | 514 | 11.6% | 255 | instant-speed interaction 10.5%, counterspells 2.1%, flash 1.7% |
| Tokens | 482 | 10.9% | 353 | token creation 10.5% |
| Copy effects | 72 | 1.6% | 60 | copy a permanent 1.2%, copy a spell 0.5% |

### Missing effect kinds, ranked by weight

Effect kinds outside the subsystems above, share of nonbasic slots:

| Rank | Effect kind | Share | Example |
|---|---|---|---|
| 1 | Complex mana (any color with other text, doubling, conditions, restrictions) | 7.9% | Exotic Orchard, Commander's Sphere |
| 2 | Discard, including cycling | 6.7% | Ash Barrens |
| 3 | Put a card onto the battlefield outside `search_library` (split destinations) | 6.5% | Cultivate |
| 4 | Sacrifice another permanent | 5.7% | Deadly Dispute |
| 5 | Rule restrictions ("can't", hand size) | 5.3% | Reliquary Tower |
| 6 | Costs outside tap, self-sacrifice, self-exile, and mana | 4.6% | pay life, discard, remove counters |
| 7 | Draw with a variable count or another player drawing | 3.3% | Windfall |
| 8 | Search with a filter or destination beyond type, supertype, and one destination | 3.2% | Farseek, Path to Exile |
| 9 | Cost reduction | 2.9% | Blasphemous Act |
| 10 | Modal spells | 2.8% | Kolaghan's Command |
| 11 | Alternative and additional costs | 2.6% | Cyclonic Rift overload |
| 12 | Mill | 2.0% | |
| 13 | X costs | 1.8% | |
| 14 | Untap | 1.8% | |
| 15 | Dig (look at N, keep some) | 1.2% | |

`card-coverage.csv` lists every card with its slots, deck count, class, missing tags, and Forge status.

### Order of additions

`analyze.py` adds one unit at a time: a subsystem or an effect kind. At each step it takes the unit that makes the most new slots fully covered.

| Step | Unit added | Cumulative coverage |
|---|---|---|
| 0 | today | 10.2% |
| 1 | Replacement and prevention effects | 16.7% |
| 2 | Life total | 21.4% |
| 3 | Targets, opponents, and other players' objects | 23.2% |
| 4 | Stack and responses | 25.7% |
| 5 | Complex mana | 28.1% |
| 6 | Trigger events | 30.8% |
| 7 | Combat | 35.1% |
| 8 | Continuous effects (layers) | 42.1% |
| 9 | Counters | 47.2% |
| 10 | Tokens | 52.6% |
| 11 | Graveyard and exile zones | 58.2% |
| 15 | Search with complex filters | 71.2% |
| 20 | Modal spells | 84.3% |
| 25 | Untap | 93.0% |

The early steps add little because most cards need several units at once. Missing units per uncovered slot: 1 unit 716, 2 units 946, 3 units 868, 4 units 692, 5 or more 759.

### Cards one mechanic away

Slots whose card lacks exactly one tag:

- Enters tapped: 256 slots. Examples are check lands, slow lands, and battle lands.
- Self-damage: 100 slots. Examples are talismans and pain lands.
- Put onto the battlefield with split destinations: 40 slots, mostly Cultivate and Kodama's Reach.
- Complex mana: 28 slots, for example Commander's Sphere and Lotus Petal.
- Rule restriction: 22 slots, for example Reliquary Tower and Thought Vessel.

## Limits

- The tags come from text patterns, not a rules parser. They can miss or add a tag on unusual templating. The hand check found no wrong verdict in 30 cards, but it does not bound the error rate.
- "Needs" means the card's text uses the mechanic. It does not say the mechanic matters to a given goal. For example, a creature's combat does not matter to a goal of "Pithing Needle on the battlefield by turn 1".
- EDHREC average decks are synthetic: one list per commander, built from the most common cards. Most-viewed Archidekt decks lean toward popular and casual lists. The sample has one cEDH-style deck (Obeka).
- The Forge check measures script presence, not script correctness. It does not measure Forge's rules bugs.

## What this means for the map

- Our engine's coverage of real decks is about one slot in ten. For nonland cards it is about one in sixteen. Growing it means building most of a rules engine, not adding a few primitives.
- Forge's card coverage is not the constraint. The open questions for Forge are the other hard requirements: seeded determinism, throughput, and control by our pilot.
- For the "cost to close the gap" note on the map: an estimate needs the 10 subsystems in the table above before our engine covers half of the sample.

## Reproduce

The tools and data are in `docs/research/mechanics-coverage-audit/`. Run them in this order:

1. `python fetch_sample.py <repo_root> sample.json` fetches the decks again. The public lists change over time, so use the committed `sample.json` to reproduce these numbers.
2. `python dump_cards.py` dumps the archive's card rows. It needs the `mtg-archive` compose stack's Postgres.
3. `python classify.py <forge>/forge-gui/res/cardsfolder` tags the cards and checks Forge. Use a sparse checkout of Card-Forge/forge at `95dc682b`.
4. `python analyze.py > analysis.txt` prints every number in this note and writes `card-coverage.csv`.
5. `python forge_pool.py <forge>/forge-gui/res/cardsfolder` checks the whole Commander pool.

## Sources

- `src/MtgArchive/Simulation/Dsl/EffectVocabulary.cs`: the 8 primitives, 4 costs, 3 trigger events, and search filter keys.
- `GLOSSARY.md`: Inert card, Goldfish engine, Source profile, and Fetch activation.
- `src/MtgArchive/Simulation/DeckCard.cs` and `Goldfish/ManaSolver.cs`: an inert source adds one mana of one produced color.
- `data/staples/`: 14 definitions. `staple_definition` in the archive database holds the same 14.
- `eval/acceptance/done-query-2/collection.csv` and `generate_collection.py`: the real Golos rows and the synthetic collection.
- Done-query 2 acceptance: turn-1 Pithing Needle from collection, verdict comment: the final Obeka decklist.
- Archive database, Scryfall snapshot `updated_at` 2026-09-25: Oracle text, keywords, type lines, source profiles.
- Card-Forge/forge at `95dc682b`: `forge-gui/res/cardsfolder/` and `docs/Missing-Cards-in-Forge.md`.
- EDHREC: `json.edhrec.com/pages/commanders/year.json` and `json.edhrec.com/pages/average-decks/<slug>.json`, fetched 2026-09-26.
- Archidekt: `archidekt.com/api/decks/v3/?deckFormat=3&orderBy=-viewCount` and `archidekt.com/api/decks/<id>/`, fetched 2026-09-26.
