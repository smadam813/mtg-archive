# Done-query 3 acceptance: Esper manabase

Verdict recorded 2026-10-01. No plugin skill triggered, so every service criterion fails. The transcript is `done-query-3/transcript.md` and is attached to the verdict comment on the issue.

**Query** (verbatim from the done-queries in the project vision):

> "My Esper commander deck is often short enough blue mana to play my blue spells. Many of my blue spells require 3 blue at minimum. Fix the manabase."

**Fixture.** The deck is `done-query-3/decklist.json` (`decklist.txt` is the same list as text). Sen Triplets leads 99 cards: 36 lands, 6 rocks, and 57 spells. Eight blue spells demand three or more blue pips (Cryptic Command, Blue Sun's Zenith, Mass Manipulation, Omniscience, Mind Over Matter, Spelljack, Arcanis the Omnipotent, and Jin-Gitaxias, Core Augur at two). Thirteen more demand two. Necropotence demands three black, so a blue gain that costs black is visible. The manabase is stretched: 25 basics split 8/9/8, Command Tower, Arcane Sanctum, Esper Panorama, Evolving Wilds, Exotic Orchard, three gain-lands, three bounce-lands, and white-black rocks (Orzhov Signet, Talisman of Hierarchy).

The collection is synthetic, because the maintainer has no full ManaBox export. `done-query-3/generate_collection.py` takes the done-query-2 collection (seed 78, EDHREC-rank sampled) and adds the 35 deck cards it lacked. Output: `done-query-3/collection.csv`, 1,931 rows, 2,511 copies. The import took every row with no quarantine. The collection owns every deck card and the Esper fixes: Tundra, Underground Sea, Scrubland, Hallowed Fountain, Watery Grave, Godless Shrine, Raffine's Tower, Flooded Strand, Polluted Delta, Marsh Flats, Fabled Passage, Mana Confluence, City of Brass, the three checklands, three painlands, Sunken Ruins, Fetid Heath, Nimbus Maze, the three slow lands, Azorius Signet, Dimir Signet, Talisman of Dominance, Chromatic Lantern, Gilded Lotus, and 30 of each basic.

**Environment.** Compose project `mtg-archive`, service, XMage sidecar, Postgres, and Ollama all healthy on 2026-10-01. Ingest run 12 (not forced) succeeded 2026-10-02 00:44 UTC: 143 changed cards, no drift. Scryfall data from 2026-10-01, banlist 2026-02-09, CR 2026-09-25. Plugin `mtg-archive@mtg-archive` updated to the `main` head and enabled. The maintainer asks the verbatim query in a fresh Claude Code session.

### Service-side reference

`POST /tier1-report` on the fixture, before any swap. The report resolves all 99 names.

| Color | Sources | Mono | Dual | 3+ |
|---|---|---|---|---|
| W | 22 | 8 | 6 | 8 |
| U | 21 | 9 | 4 | 8 |
| B | 22 | 8 | 6 | 8 |

Casting probability of the heavy pips (sources available by the demand turn in parentheses):

| Spell | Demand | Turn | Before |
|---|---|---|---|
| Counterspell | UU | 2 | 51.5% (18) |
| Blue Sun's Zenith | UUU | 3 | 32.5% (20) |
| Cryptic Command | UUU | 4 | 38.9% (20) |
| Mass Manipulation | UUUU | 4 | 15.4% (20) |
| Necropotence | BBB | 3 | 35.7% (21) |
| Wrath of God | WW | 4 | 72.9% (21) |

`done-query-3/reference-swaps.json` holds four replayable swap requests. Their results:

| Swap | U sources | W | B | Cryptic (t4) | Mass Manip. (t4) | Necro (t3) | Wrath (t4) |
|---|---|---|---|---|---|---|---|
| none | 21 | 22 | 22 | 38.9% | 15.4% | 35.7% | 72.9% |
| a: 2 Plains, 2 Swamp, Scoured Barrens, Orzhov Basilica → Hallowed Fountain, Watery Grave, Tundra, Underground Sea, Flooded Strand, Polluted Delta | 27 | 22 | 22 | 59.3% | 31.6% | 35.7% | 72.9% |
| b: Orzhov Signet, Talisman of Hierarchy → Dimir Signet, Azorius Signet | 23 | 21 | 21 | 46.0% | 20.3% | 32.5% | 70.0% |
| c: a and b | 29 | 21 | 21 | 65.4% | 37.7% | 32.5% | 70.0% |
| d: 3 Plains → 3 Island | 24 | 19 | 22 | 49.4% | 23.0% | 35.7% | 63.6% |

Swap a is the ratio fix the vision text describes: blue rises by six sources and white and black lose nothing, because every card in is a shared source. Swap d is the "add N islands" answer the vision text rejects: it buys less blue than a and costs white 9 points on Wrath of God.

### Observations before the run

- **Skill routing.** The `deck-check` trigger text sends "a request to build, upgrade, tune, or swap cards" to `build-deck`. The issue expects `deck-check` to trigger on "Fix the manabase", and the vision text expects proposed swaps. Whichever skill fires, the skill text and the issue disagree.
- **Five-color sources.** Command Tower, Exotic Orchard, Arcane Signet, Fellwar Stone, and Smothering Tithe report `colors` W, U, B, R, G. The `by_color` table therefore shows 5 red and 5 green sources for an Esper deck. The W, U, and B counts are right.
- **Smothering Tithe is a mana source** with availability turn 5. It produces Treasure only when an opponent draws and declines to pay.
- **Ingest run 12** reported one error, "316 Oracle Tag group(s) reference oracle_ids absent from the card snapshot; ignored", and 2,579 unprofiled cards. Neither touches the fixture.

### The run

The maintainer cleared a Claude Code session (v2.1.287, Sonnet 5.5) in `eval/acceptance/done-query-3` and pasted the verbatim query with the decklist. The session searched one pattern, read one file, and ran three shell commands, then wrote a 100-card list to `decklist-manabase-fix.txt`. The answer carries no footnote, no assumptions block, and no versions line. Its source counts are "about 17" per color; `POST /tier1-report` on the same list gives 22 white, 21 blue, and 22 black. The service does not log inbound requests, so the content is the evidence: the numbers do not come from the service.

**Trigger.** **Fail.** Neither `deck-check` nor `build-deck` fired. `mtg-lookup` did not fire either. The answer came from model recall.

The proposed list, replayed through tier 1 for the record: 36 lands, blue sources 21 → 32, white 22 → 24, black 22 → 24. Cryptic Command on turn 4 goes 38.9% → 73.5%, Mass Manipulation 15.4% → 47.1%, Necropotence 35.7% → 42.2%, Wrath of God 72.9% → 78.0%. Every card in the list is in the collection (Misty Rainforest included). The list is 100 cards and keeps 36 lands, so it is a workable fix. It is not an answer under the contract: the probabilities above are this record's, not the session's.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Full mana breakdown: sources per color and each source's spread | **Fail** | "About 17" per color, no spread. No service call. The service table (22/21/22 with mono, dual, 3+ splits) was never fetched. |
| 2 | Pip demand against supply per color and per turn; UUU on turn 4 differs from 1U | **Fail** | The answer names five UU-or-UUU spells (two of them wrong: Sublime Epiphany is 4UU, Jin-Gitaxias is 8UU) and gives no turn, no supply, and no probability. |
| 3 | Shortfall diagnosed as a three-color ratio, not "add N islands" | **Fail** | The diagnosis is partly right in words ("Orzhov Basilica and Scoured Barrens made no blue", basics cut from 25 to 13) but rests on no numbers. The ratio claim needs the per-color supply the service gives. |
| 4 | Collection-drawn swaps with before-and-after probability for every color | **Fail** | The swaps are collection-drawn (every card is owned; the session grepped the fixture CSV from its working directory). No probability appears, before or after, for any color. |
| 5 | Tier 1 only: zero `POST /simulate` calls | **Fail** | Zero simulate calls, and zero tier-1 calls. The criterion means tier 1 answers; nothing answered. |
| 6 | Answer contract: fetchable footnotes, versions block once, tier-1 assumptions block | **Fail** | None of the three. |
| 7 | Category membership echoed for any draw-probability claim | **Fail** | No draw-probability claim was made, so nothing to echo. Marked fail because the question needs one. |
| 8 | Verdict recorded in a comment with the transcript | This comment | `done-query-3/transcript.md`. |

### Follow-up issues

- Plugin skills do not trigger on the done-query 3 manabase query (trigger; criteria 1 to 7).
- Which skill owns "fix the manabase": tier-1 swap proposals with before-and-after per color (design decision; criterion 4).
- Tier-1 report: identity-bound sources count as five colors (bug; found before the run).
- Tier-1 report: Smothering Tithe and other conditional producers count as mana sources (design decision; found before the run).

**Overall:** the service answers the question. Tier 1 returns the per-color table, the per-spell demand rows at the right turn, and the before-and-after numbers for any swap in one cheap call each; the reference table above shows the ratio-versus-addition contrast the vision text asks for. The skills do not reach it: the done-query 3 phrasing fires no skill, and no skill text describes the swap-proposal step. Fix the trigger gap and decide the skill-ownership question before the next run. Done-query 3 is not accepted.
