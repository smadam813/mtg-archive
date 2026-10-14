# Brawl-query 1 run: Wrenn and Six to Competitive Brawl

Run recorded 2026-10-05. This is a run record, not a verdict. The calls and result below come from a working session. The trigger was tested in a fresh session, as the user reports.

**Fixture.** A Wrenn and Six deck with Ragavan, Nimble Pilferer in the 99: commander plus 99, Red-Green.
The deck is `brawl-query-1/deck.txt`. Legality captures are in `brawl-query-1/legality.json`.
**Environment.** Compose stack up. Plugin `mtg-archive@mtg-archive`. Brawl legality as of 2026-10-05 (Scryfall). Popularity source untapped, as of 2026-10-04.

**Question.** "I want to take my Wrenn and Six Brawl deck into Competitive Brawl, but Wrenn and Six is banned there. What are my alternatives?" The agent invoked `mtg-archive:commander-swap` and read the deck from the fixture file.

### Calls

1. `GET /healthz`: ok.
2. `POST /decklist-parse` with `format` `competitive_brawl`: no notes, no name rewrites.
3. `POST /commander-alternatives`, limit 200, `collection_scope` false: 200 candidates, `deck_size` 99.
4. `GET /cards/{Wrenn and Six}`: `competitive_brawl` legality is `banned` for the `commander` and `main` roles, with a `legality` citation.
5. `POST /cards/semantic-search` with the Wrenn and Six Oracle text, role `commander`, k 200: 178 of 200 candidates got a distance.
6. `GET /cards/{id}` for each of the five presented candidates.

### Result

| Tier (retained, color identity distance) | Candidates |
|---|---|
| 98, 0 | 78 |
| 98, 1 | 65 |
| 98, 2 | 7 |
| 98, 3 | 44 |
| 71, 1 | 6 |

The top tier is below the pull depth, so the comparison covers the whole tier.

The five presented candidates, in re-ranked order, each keep 98 of 99 and lose only Ragavan, Nimble Pilferer (`banned`, `legality` citation):

| # | Candidate | Popularity rank | Distance |
|---|---|---|---|
| 1 | Toph, Hardheaded Teacher | 3203 | 0.426 |
| 2 | Rydia, Summoner of Mist | 4090 | 0.428 |
| 3 | Borborygmos Enraged | 7533 | 0.462 |
| 4 | Hugs, Grisly Guardian | 4162 | 0.474 |
| 5 | Radha, Heart of Keld | 5747 | 0.483 |

Toph and Rydia are within 0.003, a tie. The service's first candidate, Minsc & Boo, Timeless Heroes, is in the same tier but its distance is larger, so it is not in the five.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Format and move read stated at the top | Pass | "Treating 'Competitive Brawl' as the `competitive_brawl` format", with the move named. |
| 2 | Ranked candidates via retention, five presented | Pass | 200 pulled, five presented in tier-first order. |
| 3 | Old commander's Competitive Brawl status cited | Pass | Wrenn and Six `banned`, `legality` citation resolved with `GET /cards/{id}`. |
| 4 | Ragavan flagged as a loss with its legality citation | Pass | Every presented candidate lists Ragavan, `banned`, with a footnote. |
| 5 | Agent-side function re-rank, measure named | Pass | Semantic distance named. The tie of Toph and Rydia is stated. |
| 6 | Collection scope stated | Pass | "Collection scope: false." |
| 7 | Versions block carries `legality_as_of`, popularity source | Pass | One block at the end. |
| 8 | Fresh-session trigger | Pass (user report) | The user ran the question in a fresh session. The session version, first action, and answer time are not recorded here. |

### Observations

- The semantic-search response `versions` block reads `format` commander, banlist 2026-02-09, popularity edhrec. The skill's call sends no format, so the block does not describe the request. The answer used the `commander-alternatives` block, which was correct. Decision for the owner: whether the semantic call should take `format`, or the skill should ignore that block.
- The top tier is 78 commanders, so retention barely separates the candidates here. The function re-rank decides the answer.
- A Brawl `banned` loss carries a `legality` citation, not a ban event. The answer rendered it so.
- The deck has 99 cards, so no deck-size gap applied.

### Answer

Treating "Competitive Brawl" as the `competitive_brawl` format, as a move. Wrenn and Six is banned as commander and as a main-deck card.[^1]

Top five, each keeping 98 of 99 in Red-Green and losing only Ragavan, Nimble Pilferer (banned):[^2]

1. Toph, Hardheaded Teacher: rank 3203, distance 0.426. Returns an instant or sorcery from the graveyard on entry.
2. Rydia, Summoner of Mist: rank 4090, distance 0.428. Landfall loot. A tie with Toph.
3. Borborygmos Enraged: rank 7533, distance 0.462. Discards a land to deal 3 damage to any target.
4. Hugs, Grisly Guardian: rank 4162, distance 0.474. Plays an additional land each turn.
5. Radha, Heart of Keld: rank 5747, distance 0.483. Plays lands from the top of the library.

Assumptions: pull depth 200, presentation depth 5, top tier of 78 below the pull, similarity is semantic distance, collection scope false, popularity Untapped as of 2026-10-04.

Versions: format competitive_brawl, Brawl legality as of 2026-10-05 (Scryfall), popularity untapped as of 2026-10-04.

[^1]: Wrenn and Six: "competitivebrawl banned as of 2026-10-05." `GET /cards/108ae90a-50fa-4cfd-b751-d630e41425fe`.
[^2]: Ragavan, Nimble Pilferer: "competitivebrawl banned as of 2026-10-05." `GET /cards/37108cd4-bbab-4ce3-9ed6-f60e8422e703`.

### Probe: legality of all 200 candidates and the mono-color path

Run 2026-10-05 against the same `commander-alternatives` response. It reuses that response; no new service state.

- **Legality.** `GET /cards/{id}` for all 200 candidates: the `competitive_brawl` `commander` role is `legal` for every one. 12 candidates are planeswalkers (for example Minsc & Boo, Timeless Heroes; Domri, Anarch of Bolas; Xenagos, the Reveler). None is a Vehicle or Spacecraft, so Vehicle eligibility is not shown by this fixture.
- **Mono-color candidates.** 6 of 200 are mono-green and none is mono-red: Rofellos, Llanowar Emissary (rank 14); Fynn, the Fangbearer; Tifa Lockhart; Mole Man, Moloid Master; The Notary Hobbits; Azusa, Lost but Seeking. Each keeps 71 of 99 and loses 28.
- **Rofellos losses.** Ragavan, Nimble Pilferer: `banned`, `legality` citation "competitivebrawl banned as of 2026-10-05". 27 cards in 21 entries: `color_identity`, `card` citation. The red cards are Lightning Bolt, Abrade, Chaos Warp, Blasphemous Act, Goblin Bombardment, Goldspan Dragon, Terror of the Peaks, Faithless Looting, Thrill of Possibility, Jeska's Will, Professional Face-Breaker, Talisman of Impulse, Unexpected Windfall, Big Score. The lands are Stomping Ground, Cinder Glade, Rootbound Crag, Karplusan Forest, Game Trail, Temple of Abandon, and 7 Mountain.
- **Tiers.** The mono-green candidates sit in the (71, 1) tier, below the 98-card tiers, so the answer's top 5 never shows them. The color-identity loss path fires only for a candidate chosen from lower tiers.

### Probe: Vehicle and Spacecraft eligibility

Run 2026-10-05. The frozen fixture pulls no Vehicle in its 200 candidates, because retention ranks them below the pull. This probe uses a derived deck, not the frozen one.

- **Pool.** `POST /cards/search` with `format` `competitive_brawl`, `role` `commander`, `types` `["Vehicle"]` returns 17 cards, and `["Spacecraft"]` returns 6. Examples: Esika's Chariot (G), Skysovereign, Consul Flagship, The Regalia, Dawnsire, Sunstar Dreadnought, Hearthhull, the Worldseed (B, G, R).
- **Derived deck.** The fixture without the 28 cards Rofellos, Llanowar Emissary loses (Ragavan and the 27 red cards): 70 entries, 71 cards, same commander. Mono-green candidates then keep the whole deck and rank above the red cards' tiers.
- **Result.** `POST /commander-alternatives`, limit 200, scope false: 200 candidates, all `legal` as Competitive Brawl commanders by `GET /cards/{id}`. Two are not creatures: Esika's Chariot (G, 4/4, "Legendary Artifact — Vehicle", legality citation "competitivebrawl legal as of 2026-10-05") at position 152, keeps 71 of 71, no losses; and Hearthhull, the Worldseed (B, G, R, "Legendary Artifact — Spacecraft") at position 159, keeps 71 of 71, no losses.
- **Limit.** Neither card is in the top 5 of the derived pull. The probe shows that the service returns them as eligible candidates. It does not show the skill presenting one.
