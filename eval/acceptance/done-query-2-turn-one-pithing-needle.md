# Done-query 2 acceptance: turn-1 Pithing Needle from the collection

Verdict recorded 2026-09-25. The transcript is attached to the verdict comment on the issue.

**Fixture.** A synthetic collection, because the maintainer has no full ManaBox export. `done-query-2/generate_collection.py` (seed 78) keeps the maintainer's real Golos deck rows, forces Pithing Needle, and samples every paper Commander card by EDHREC rank, with fewer copies of cards above $15. Output: `done-query-2/collection.csv`. The import took 1,890 of 1,896 rows (2,469 copies). Six rows were quarantined as `ambiguous` against same-name tokens. The collection owns the Grixis turn-1 package (Dark Ritual, Demonic, Vampiric and Mystical Tutor, Gamble, Simian Spirit Guide, blue cantrips). It lacks Lotus Petal, Chrome Mox, Mox Diamond, Mana Vault, and Imperial Seal.

**Environment.** The Docker volumes were empty, so the database was rebuilt from scratch: compose project `mtg-archive` with the GPU overlay. Ingest runs 1 to 3 succeeded 2026-09-25. Banlist version 2026-02-09, bracket spec 2025-10-21, Game Changers snapshot 2026-02-09, staple cut 2026-09-25.3. Plugin `mtg-archive@mtg-archive`. The maintainer asked the verbatim query in a fresh Claude Code session.

**DSL-coverage precondition.** The preliminary `build-deck` run (Obeka, Grixis, seed 20260924) had 4 of 97 cards defined. The floor was >= 17.6%, CI [0.154, 0.201]. A later PR authored Dark Ritual and parked 11 enablers the vocabulary cannot express. After the re-ingest, the same deck had 5 defined and a floor of >= 18.0%, CI [0.157, 0.205]. The precondition stops here: the rest waits for vocabulary growth.

**Trigger.** Pass. `build-deck` triggered first, not `deck-check`.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Legal decklist from owned cards only, availability respected | Pass | All 99 names appear in `GET /collection` (1,890 cards). Every nonbasic has `available` 1 and one copy; each basic has 1 copy of 30 owned. A replay of the list returns `legality.verdict: legal` with no violations. |
| 2 | Floor-labeled probability with 95% CI, run count, and base seed | Partial | The results table gives floors with 95% intervals, 1,000 runs, and base seed 20260924. The lead says "about 20% of games", a point estimate. Every label drops the service's ", greedy policy" qualifier. |
| 3 | Mulligan and play policy named, up to 3 replayable exemplar lines | **Fail** | The answer names neither `goal-aware` nor `goal-aware-greedy`. The envelope returns 3 exemplars; the answer shows none. |
| 4 | "Competition-ready" → bracket 4, mapping declared, bracket floor with per-barometer reasons, phrase never sent | Partial | Declared: "treating 'competition-ready' as bracket 4". The Game Changers barometer is named with its 9 cards, and a replay confirms `bracket_floor` 4 with one `game_changers_count` reason. The answer says "classifies as bracket 4", not a floor. The transcript does not show the requests, so "phrase never sent" is unverified. The skill forbids sending it. |
| 5 | "Consistently" → declared numeric target; budget, target, and stopping criteria up front | Pass | The frame block comes first: target 0.80 ("consistently"), budget 15, and the three stopping criteria. |
| 6 | Plateau or budget stop reports best-so-far and the gap; success needs a fresh-seed confirmation | Partial | Best-so-far and the gap are stated (0.80 vs 0.189, a gap of 0.61), and a fresh-seed call (8675309) is counted. The stop reason "early stop, target out of reach" is not one of the three criteria. Calls 5 to 7 still rose, so no plateau had fired. |
| 7 | DSL coverage surfaced; no goal-critical card inert in the final run | **Fail** | The answer reports 5 defined and names six inert enablers. Ten goal-critical cards stay inert: Vampiric and Mystical Tutor, Gamble, Simian Spirit Guide, Spoils of the Vault, Tainted Pact, Ponder, Preordain, Opt, Gitaxian Probe. All ten are parked for missing vocabulary. The answer calls them "queued"; they are parked. |
| 8 | Answer contract: fetchable footnotes, versions block once, assumptions where they bite | Partial | Two footnotes, both fetchable: `GET /game-changers/2026-02-09` and `GET /bracket-spec/2025-10-21` return 200. No card carries a citation. One versions block with banlist, bracket spec, Game Changers, and staple cut. The engine simplifications and tier-1 assumptions are stated. The floor-label qualifier is missing (criterion 2). |
| 9 | Verdict recorded in a comment with the transcript | This comment | Transcript attached. |

### Other observations

- **Entry order changes the result.** A replay of the final 99 at seed 20260924 gives lower bounds of 0.174, 0.183, and 0.181 for three orderings of the same cards. `GameSetup.Start` filled the library in decklist order before the seeded shuffle. The loop acted on differences of the same size (land counts 32 to 40: 0.155 to 0.179). Its swap decisions are therefore not attributable to the cards, and the reported numbers cannot be reproduced from the rendered list. The fix for the entry-order bug changes the numbers for seed 20260924. Every number in this record comes from the engine before that fix. Nobody re-ran the acceptance.
- **The target cannot be met.** A singleton turn-1 goal cannot reach 0.80, even with every enabler authored. Tier-1 put the draw of Needle or a to-hand tutor by turn 1 at 35%. The answer said so plainly and gave the reason.
- **The session ran a build.** The fresh session ended with a `dotnet restore` and a build of the worktree. That is unrelated to the question and changed no files.
- **The ingest reported drift.** It showed 42 `banlist` drift entries and `pinned_oracle_text` drift on Pithing Needle and Evolving Wilds from Scryfall's self-reference templating. Neither affects this deck's result.

### Follow-up issues

- Simulate result depends on decklist entry order (bug).
- build-deck skill text: policies, exemplars, card citations, full floor label, bracket as a floor, queued vs parked (criteria 2, 3, 4, 7, 8).
- build-deck has no stopping criterion for a target tier-1 proves out of reach (criterion 6). Design decision.
- Effect DSL vocabulary for the parked turn-one enablers (criterion 7).
- Check corpus rejects Phyrexian and hybrid mana costs (criterion 7, Gitaxian Probe).
- Collection import: token names quarantine real cards.
- Banlist drift: 42 cards without events.
- Staple errata for Pithing Needle and Evolving Wilds.

**Overall:** the flow runs end to end. It returns a legal, owned, bracket-4 list, declares its frame up front, and gives an honest gap. Criteria 3 and 7 fail. Criterion 3 is skill text. Criterion 7 waits on vocabulary growth. Four criteria are partial, mostly on the answer contract and the stop rule. Fix the entry-order bug before the next acceptance run: until then, lower bounds at one seed are not comparable across decklists.

### Re-run on our engine (XMage migration phase 0, 2026-09-27)

The parity gate needs a done-query 2 number from our engine on current `main` before XMage replaces it. The deck is `done-query-2/final-99.json`, the final 99. The request is `done-query-2/rerun-request.json`: `on_battlefield` Pithing Needle by turn 1, 10,000 runs, seed 20260924, horizon 10, goal-aware mulligan.

| Engine | Floor | 95% CI | Turn 1 | Turn 3 | Defined |
|---|---|---|---|---|---|
| `main` before XMage | >= 21.7%, greedy policy | 0.2090–0.2252 | 0.2170 | 0.3684 | 11 of 99 |

Versions: banlist 2026-02-09, staple cut 2026-09-25.6. This matches the earlier PR record (2,170 successes). The run used branch `xmage/0-baseline-and-contracts`. That branch changes no engine code from the `main` commit it branches from, so the table cites that engine.

### Re-run on XMage (XMage migration phase 4, cutover, 2026-09-30)

Spec §8 step 1: the same request (`done-query-2/rerun-request.json`, 10,000 runs, seed 20260924, horizon 10, goal-aware mulligan) on the engine that now serves `/simulate`. The two floors are not comparable seed for seed (ADR 0011); the parity gate compared the engines at 10,000 runs on frozen decks (`eval/parity/cutover/report.md`).

| Engine | Version | Floor | 95% CI | Turn 1 | Turn 3 | Unimplemented | Unprofiled | Engine errors |
|---|---|---|---|---|---|---|---|---|
| xmage | xmage-1.4.61V1+09f8be52 | >= 19.5%, greedy policy | 18.77%–20.33% | 19.54% | 44.34% | 0 | 13 | 0 |

Versions: banlist 2026-02-09.
