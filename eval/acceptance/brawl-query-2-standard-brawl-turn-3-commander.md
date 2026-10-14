# Brawl-query 2 runs: Standard Brawl deck from the collection, commander cast on turn 3

Run recorded 2026-10-06. This is a run record, not a verdict. Each run came from a fresh session, as the user reports. The query was in natural words. In run 1, the agent's first action was to invoke `mtg-archive:build-deck`. Runs 2 and 3 were pasted from the user's sessions, and each run's section says what was checked in the working session afterwards.

**Fixture.** The synthetic ManaBox collection `brawl-query-2/collection.csv`: 3,756 cards, 5,257 copies. The live collection matched it by name and count before the run. The built decks are `brawl-query-2/final-deck-loot.txt` (run 1) and `brawl-query-2/final-deck-jolly-balloon-man.txt` (run 2), and `brawl-query-2/final-deck-jolly-balloon-man-run3.txt` (run 3), and `brawl-query-2/final-deck-jolly-balloon-man-run4.txt` (run 4).
**Environment.** Compose stack up. Plugin `mtg-archive@mtg-archive` for run 1. The plugin version of run 2 is not recorded. Run 3 used the `build-deck` edits. Brawl legality as of 2026-10-05 (Scryfall). Popularity source edhrec, as of 2026-10-05, used as a proxy.

Four runs follow. Run 1 let the agent choose the commander (Loot, Exuberant Explorer). Runs 2 and 3 used the frozen commander, The Jolly Balloon Man, in fresh sessions. Run 3 used the `build-deck` edits (25 life, a footnote on every card). Run 4 used the skill with both changes.

## Run 1: agent-chosen commander

**Question.** "Build me a Standard Brawl deck around that consistently casts its commander on turn 3." The agent invoked `mtg-archive:build-deck` with the collection file.

### Frame

- Format: `standard_brawl`, read from "Standard Brawl". Commander plus 59 cards.
- Goal: `cast(Loot, Exuberant Explorer, 3)`.
- Target: lower bound at least 0.80 ("consistently"). No power target: Arena publishes no bracket system for Brawl.
- Budget: 15 simulate calls; 6 spent. Base seed 20261006. Confirmation seed 8675309. Stopped on: target met, confirmed.
- Commander: Loot, Exuberant Explorer ({2}{G}, mono-green), the most popular owned commander at mana value 3 or less.
- Reach bound: not applicable. The commander starts in the command zone.

### Calls

1. `GET /healthz`: ok. `GET /collection`: matches the fixture.
2. `POST /cards/search`, role `commander`, `standard_brawl`, scope true, mana value at most 3: 100 candidates, ranked by `popularity_rank`.
3. `POST /cards/search` for ramp, removal, draw, creatures and lands in identity G, scope true.
4. `POST /tier1-report` on the final deck, max turn 4.
5. `POST /simulate`, 1,000 runs, horizon 4, six calls on four lists (below).
6. `POST /cards/search` by name for each card citation.

### Result

| Round | Change from the draft | Seed | Label | Lower bound |
|---|---|---|---|---|
| 1 | Draft: 23 Forest, 36 spells (59 cards) | 20261006 | >= 81.4%, greedy policy | 78.9% |
| 2 | 24 Forest; add Poison Dart Frog, Springleaf Drum; cut Icetill Explorer, Dwynen, Gilt-Leaf Daen, Mightform Harmonizer | 20261006 | >= 79.9%, greedy policy | 77.3% |
| 3 | 25 Forest; cut Solemn Simulacrum, Burnished Hart, Archdruid's Charm; add Poison Dart Frog, Springleaf Drum, a Forest | 20261006 | >= 88.1%, greedy policy | 85.9% |
| 3 confirm | Same list | 8675309 | >= 87.3%, greedy policy | 85.1% |
| 4 | Cut Studious First-Year // Rampant Growth (blank card; the list was 61 cards with the commander, and the format allows 60) | 20261006 | >= 87.4%, greedy policy | 85.2% |
| 4 confirm | Same list | 8675309 | >= 86.3%, greedy policy | 84.0% |

**Standard Brawl floor (final list).** >= 87.4%, greedy policy. 95% interval 85.2% to 89.3%, 1,000 runs, base seed 20261006. Engine xmage, version xmage-1.4.61V1+ec6a3165. Format `standard_brawl`. Confirmation: >= 86.3%, greedy policy, interval 84.0% to 88.3%, seed 8675309, same engine and version.

Round 3 and its confirmation ran on a 60-card main deck, one card over the limit of 59. Only round 4 and its confirmation ran on a legal list. Cite the round 4 numbers.

Final deck: 59 cards plus the commander. 25 Forest and 34 spells: 8 ramp, 6 fight and removal, 5 card flow, 15 creatures. `POST /deck-verdict` (in the simulate envelope): `legal`, deck size 60 of 60, no card findings, no shape findings.

Engine coverage: unimplemented none. Unprofiled: Bloom Tender, Elvish Archdruid, Fierce Empath, Inspiring Call, Outcaster Trailblazer, Terrasymbiosis, The Earth King, Wood Elves. Bloom Tender and Wood Elves feed the goal, so the floor may understate the deck. Engine errors: 0.

Policies: play `goal-aware-greedy`, mulligan `goal-aware`. Curve of the base-seed run: turn 2 cumulative 9.9% to 11.6% across rounds, turn 3 about 86% to 88%, turn 4 about 96% to 98%.

Exemplars of round 3 (base seed): fastest, seed -1820933671, run 13, cast on turn 2. Typical, seed -1716817843, run 434, cast on turn 3. Failure, seed 1711968704, run 6, no cast by turn 4.

Tier-1 (final list): land drops 98.4% on turn 1, 93.1% on turn 2, 83.1% on turn 3, 69.3% on turn 4. At least one of nine ramp cards by turn 3: 80.1%. Tier-1 assumes no mulligans, so it does not bound the simulated floor.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Format read stated at the top | Pass | "Standard Brawl" read as `standard_brawl`. |
| 2 | No bracket step, no power target | Pass | The answer states Arena has no bracket system for Brawl. |
| 3 | Format sent on every call | Pass | Every search, tier-1 and simulate call carried `standard_brawl`. |
| 4 | Collection scope on every candidate query | Pass | Every query sent `collection_scope` true. |
| 5 | Floor labelled with interval, runs, seed, engine, version | Pass | Stated in the answer. |
| 6 | Confirmation on a fresh seed | Pass | Seed 8675309, lower bound 84.0%. |
| 7 | Deck verdict legal before the answer | Pass after fix | The first list had 61 cards. The fix is round 4. |
| 8 | Blank card replaced | Pass | Studious First-Year // Rampant Growth was unimplemented and was cut. |
| 9 | Fresh-session trigger | Pass (user report) | The user reports a fresh session. The agent invoked `mtg-archive:build-deck` as its first action. The session version and answer time are not recorded here. |
| 10 | Frozen BQ2 commander | Not met | The request named no commander. The agent chose Loot, Exuberant Explorer. The fixture's `parity-deck.txt` names The Jolly Balloon Man. |
| 11 | Answer names the game as a Brawl game (25 life, free mulligan, no turn-1 draw) | Partial | The answer stated the free mulligan and no turn-1 draw. It did not state 25 starting life. |

### Observations

- The `/simulate` verdict caught the 61-card list only after rounds 1 to 3. The agent built the list by hand and miscounted lands. Decision for the owner: whether the skill should call `POST /deck-verdict` before the first simulate call.
- The `ramp` tag returns many non-ramp cards (for example Fountainport and Monument to Endurance). The agent screened them by Oracle text.
- The 25th Forest entered through a swap that named Forest as a spell. The final decklist merges it into one Forest entry of 25.

### Versions

Format standard_brawl, Brawl legality as of 2026-10-05 (Scryfall), popularity edhrec as of 2026-10-05, engine xmage-1.4.61V1+ec6a3165.

## Run 2: frozen commander, fresh session

Recorded 2026-10-06 from the output the user pasted from a fresh session. The session transcript is not in the repo. The deck check below was rerun in the working session against the live service.

**Question.** "Build me a Standard Brawl deck around The Jolly Balloon Man that consistently casts its commander on turn 3." The collection is the fixture.

### Frame

- Format: `standard_brawl`. No power target. Goal: `cast(The Jolly Balloon Man, 3)`, on the play. Target: lower bound at least 0.80 ("consistently").
- Budget: 15 simulate calls; 10 spent. Base seed 20261006. No confirmation call, because the target was not met.
- Stopped on: plateau. Three rounds in a row failed to beat a lower bound of 0.747.
- Reach bound: not applicable. The commander starts in the command zone.

### Result

**Standard Brawl floor.** >= 77.4%, greedy policy. 95% interval 74.7% to 79.9%, 1,000 runs, base seed 20261006. Engine xmage 1.4.61V1+ec6a3165. Format `standard_brawl`. 0 engine errors.

The target is not met. The gap is 0.800 minus 0.747, which is 0.053.

- First list, many one- to three-mana spells: >= 45.2%. The pilot cast a cheap spell in place of the commander.
- Spells moved to mana value 4 and up: >= 75%.
- Fabled Passage swapped for a Plains: >= 77.4%. A failing line had the Passage fetch a Mountain and leave no white source.
- Rounds v5 to v9 (more lands, fewer tapped duals) stayed within about 1 to 6 points of the best.

Engine coverage: unimplemented none. Unprofiled: Hoarding Dragon, Magmatic Hellkite, Settle the Wreckage, Solemn Simulacrum, Starfield Shepherd. None is a goal piece. Policies: play `goal-aware-greedy`, mulligan `goal-aware`. Mulligan looks 4.

Final deck (`brawl-query-2/final-deck-jolly-balloon-man.txt`): The Jolly Balloon Man, red-white, plus 59 cards: 26 lands (Command Tower, Sacred Foundry, Sundown Pass, Temple of Triumph, Elegant Parlor, 10 Mountain, 11 Plains), 3 ramp, 20 creatures, 10 other spells.

### Deck check

I rebuilt the list from the pasted answer and checked it in the working session.
- `POST /deck-verdict`, `standard_brawl`: `legal`, deck size 60 of 60, no card findings, no shape findings.
- Every card name is in `brawl-query-2/collection.csv`.
- The answer said 17 creatures and 11 spells. The listed names are 20 creatures and 10 spells. The total of 59 is right.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | `build-deck` skill fires | Pass (user report) | The answer follows the skill's frame. The first action is not in the paste. |
| 2 | 60 cards with the commander, singleton, legal, owned, verdict `legal` | Pass | Deck check above. |
| 3 | Floor label, 95% interval, run count, base seed | Pass | >= 77.4%, greedy policy; 74.7% to 79.9%; 1,000 runs; seed 20261006. |
| 4 | Names the game as Brawl: 25 life, free mulligan, no turn-1 draw | Partial | The answer states the free mulligan and the play with no turn-1 draw. It does not state 25 life. |
| 5 | "Consistently" maps to a numeric target | Pass | 0.80. |
| 6 | Record holds the floor, engine version and format | Pass | Above. |
| 7 | Every card carries a fetchable citation | Fail | The answer cites only the commander. It says the other cards "resolve by exact name". |
| 8 | One versions block | Pass | Format standard_brawl, Brawl legality as of 2026-10-05 (Scryfall), popularity edhrec as of 2026-10-05. |
| 9 | Target met | Not met | Lower bound 74.7% against 80%. |

### Observations

- The answer's counts do not match its list (17 creatures and 11 spells, against 20 and 10). The deck itself is sound.
- The answer cites one card of 60. The answer contract asks for a footnote on every card in the decklist.
- The answer did not name 25 starting life. The simulate envelope carries it in its simplifications only as the opponent's life.
- "Fabled Passage" is not in the final list, and the answer does not say it was cut. The Plains swap is the stated reason.
- A mixed-identity red-white deck with 26 lands and a three-mana, three-color-pip commander misses 0.80 with this collection. The skill reports best-so-far and the gap, as designed.

## Run 3: frozen commander, fresh session, updated skill

Recorded 2026-10-06 from the output the user pasted from a fresh session. The session had the `build-deck` edits installed. The transcript is not in the repo. The deck check below was rerun in the working session.

**Question.** The run 2 query, unchanged: "Build me a Standard Brawl deck around The Jolly Balloon Man that consistently casts its commander on turn 3."

### Frame

- Format `standard_brawl`. No power target. Goal `cast(The Jolly Balloon Man, 3)`. Target: lower bound at least 0.80.
- Budget: 15 simulate calls; 9 spent. Base seed 20261006. No confirmation call. Stopped on: plateau after 3 rounds without a rise (rounds 7 to 9).
- Reach bound: not applicable. The commander starts in the command zone.

### Result

**Standard Brawl floor.** >= 52.2%, greedy policy. 95% interval 49.1% to 55.3%, 1,000 runs, base seed 20261006. Engine xmage 1.4.61V1+ec6a3165. Format `standard_brawl`. 0 engine errors.

The target is not met. The gap is 0.800 minus 0.491, which is 0.309. The lower bound rose from 0.411 (first list) to 0.491 (round 6). Rounds 3, 5 and 7 to 9, which added lands or fixers, did not raise it. Adding Zookeeper Mechan and Prophetic Prism gave the largest gain.

Engine coverage: unimplemented Thrór's Map, a blank card that is still in the final list. Unprofiled: Embermouth Sentinel, Erode, Silver Deputy, Troop of Ponies, Tweeze, Volatile Wanderglyph. Policies: play `goal-aware-greedy`, mulligan `goal-aware`. Exemplars: fastest, seed -135961223, run 3, turn 3; typical, seed 751116867, run 491, turn 3; failure, seed 447073765, run 0, no cast by turn 4.

Final deck (`brawl-query-2/final-deck-jolly-balloon-man-run3.txt`): 28 lands (6 duals, 11 Mountain, 11 Plains), 11 ramp and mana cards, 9 removal spells, 11 creatures and draw spells.

### Deck check

I rebuilt the list from the pasted answer in the working session.
- `POST /deck-verdict`, `standard_brawl`: `legal`, deck size 60 of 60, no card findings, no shape findings.
- Every card name is in `brawl-query-2/collection.csv`.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | `build-deck` skill fires | Pass (user report) | The answer follows the skill's frame. |
| 2 | 60 cards with the commander, singleton, legal, owned, verdict `legal` | Pass | Deck check above. |
| 3 | Floor label, 95% interval, run count, base seed | Pass | >= 52.2%, greedy policy; 49.1% to 55.3%; 1,000 runs; seed 20261006. |
| 4 | Names the game as Brawl: 25 life, free mulligan, no turn-1 draw | Pass | "Brawl starts at 25 life. That is the Brawl rule, not a service result." The sidecar's free mulligan and no turn-1 draw are stated. |
| 5 | "Consistently" maps to a numeric target | Pass | 0.80. |
| 6 | Record holds the floor, engine version and format | Pass | Above. |
| 7 | Every card carries a fetchable citation | Partial | 39 of 40 cards. Thrór's Map has none (see Observations). |
| 8 | One versions block | Pass | Format standard_brawl, Brawl legality as of 2026-10-05 (Scryfall), popularity edhrec as of 2026-10-05, with the proxy disclosure. |
| 9 | Target met | Not met | Lower bound 49.1% against 80%. |

### Observations

- The missing citation comes from the session's shell, not the service. A search by exact name for "Thrór's Map" from a UTF-8 request returns `925cacdc-6d09-4fbd-9258-9f0539da180c`, and the card is in the collection. The session's name search returned nothing, probably because of the "ó" in a Windows shell.
- The final deck holds a blank card. The loop stopped on plateau with 6 simulate calls unspent and no round that swapped Thrór's Map out. The floor is for a deck with a dead slot.
- This run scored far below run 2 (>= 77.4%, lower bound 74.7%). Run 2 found that moving spells to mana value 4 and up raised the floor. This loop did not. The loop is the agent's judgment, so the result varies between sessions.
- The XMage sidecar ran out of Java heap during the first simulate call. Its process died while the container still showed healthy. The agent restarted `mtg-archive-xmage-1` before each later run. One run attempt returned no result. This is a service fault for a separate issue.

## Run 4: frozen commander, fresh session

Recorded 2026-10-06 from the output the user pasted from a fresh session. The plugin was `mtg-archive@mtg-archive`, which holds both changes. The transcript is not in the repo. The deck check and the seed replay below were rerun in the working session.

**Question.** The run 2 query: "Build me a Standard Brawl deck around The Jolly Balloon Man that consistently casts its commander on turn 3." The collection is the fixture.

### Frame

- Format `standard_brawl`. No power target. Goal `cast(The Jolly Balloon Man, 3)`, on the play. Target: lower bound at least 0.80.
- Budget: 15 simulate calls; 8 spent. Base seed 20261006. Confirmation seed 8675309. Stopped on: target met, confirmed.
- Reach bound: not applicable. The commander starts in the command zone.
- Game: 25 starting life (stated as the Brawl rule), one free mulligan, no draw on turn 1 on the play.

### Result

**Standard Brawl floor.** >= 83.6%, greedy policy. 95% interval 81.2% to 85.8%, 1,000 runs, base seed 20261006. Engine xmage 1.4.61V1+ec6a3165. Format `standard_brawl`. 0 engine errors.

Confirmation: >= 83.6%, greedy policy, interval 81.2% to 85.8%, 1,000 runs, seed 8675309. Both calls give 836 successes of 1,000. This is the first run with the frozen commander whose lower bound reaches 0.80.

What moved the floor, from the answer:
- First list, cheap burn and two-mana spells: >= 47.8%. The pilot cast cheap spells and played tapped lands.
- Cheap spells replaced with spells of mana value 4 and up, tapped duals replaced with Starting Town: >= 78.5%.
- Gravestone Strider, Three Tree Mascot and Embermouth Sentinel added: >= 82.4%. A single red or white source fixes a land of the wrong color.
- Silver Deputy added: >= 83.6%.

Engine coverage: unimplemented none. Unprofiled: Embermouth Sentinel, Silver Deputy, Magmatic Hellkite, Settle the Wreckage, Solemn Simulacrum, Starfield Shepherd. None is the goal piece. Policies: play `goal-aware-greedy`, mulligan `goal-aware`.

Final deck (`brawl-query-2/final-deck-jolly-balloon-man-run4.txt`): 26 lands (Command Tower, Sacred Foundry, Sundown Pass, Starting Town, 11 Mountain, 11 Plains), 7 mana and fixing cards, 26 spells of mana value 4 and up.

### Deck check

I rebuilt the list from the pasted answer in the working session.
- `POST /deck-verdict`, `standard_brawl`: `legal`, deck size 60 of 60, no card findings, no shape findings.
- Every card name is in `brawl-query-2/collection.csv`.
- I ran the list at seeds 20261006 and 8675309, 1,000 runs each. Both return >= 83.6%, greedy policy, with 836 successes and a lower bound of 0.8118. The exemplar seeds differ between the two calls, so the second call used its own seed. The identical count is a coincidence.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | `build-deck` skill fires | Pass (user report) | The answer follows the skill's frame. |
| 2 | 60 cards with the commander, singleton, legal, owned, verdict `legal` | Pass | Deck check above. |
| 3 | Floor label, 95% interval, run count, base seed | Pass | >= 83.6%, greedy policy; 81.2% to 85.8%; 1,000 runs; seed 20261006. |
| 4 | Names the game: 25 life, free mulligan, no turn-1 draw | Pass | "25 starting life (a Brawl rule, not a service result), 1 free mulligan, and no draw on turn 1 on the play". |
| 5 | "Consistently" maps to a numeric target | Pass | 0.80. |
| 6 | Record holds the floor, engine version and format | Pass | Above. |
| 7 | Every card carries a fetchable citation | Pass | 40 of 40: the commander and 39 distinct cards, each with `GET /cards/{id}`. |
| 8 | Target met and confirmed | Pass | Lower bound 81.2% on the base seed and on the confirmation seed. |
| 9 | One versions block | Pass | Format standard_brawl, Brawl legality as of 2026-10-05 (Scryfall), engine xmage-1.4.61V1+ec6a3165. |

### Observations

- The answer labels its exemplars "base seed 20261006". It says the fastest line, seed -111205552, comes from the confirmation call. The typical and failure seeds (1195462616 and -1067539444) also come from the confirmation call. In my replay the base call's exemplars are 608186166, 1549769632 and 447073765.
- The answer's tier-1 assumptions are for the 26-land list. The land drops are 98.7%, 94.4%, 85.8% and 73.4% on turns 1 to 4. Tier 1 assumes no mulligans, so it does not bound the floor.
- Runs 2 and 3 scored 77.4% and 52.2%. The run 4 loop found the same fix as run 2, moving spells to mana value 4 and up, and then added fixers that let one red or white source cover the wrong color. The result varies between sessions.
- This record does not cover the skill gaps that came from prompts 2 and 3 of the same test round.
