# Done-query 1 acceptance: banned commander alternatives

Verdict recorded 2026-09-20. The transcript is attached to the closing comment on the issue.

**Fixture.** A Golos, Tireless Pilgrim superfriends deck: commander plus 99, five-color, no other banned card in the 99.
The deck is `done-query-1/decklist.json`, captured 2026-09-28 for the XMage parity gate.
**Environment.** Compose stack up. Ingest run 23 succeeded 2026-09-20. Banlist version 2026-02-09. Plugin `mtg-archive@mtg-archive` installed. Fresh Claude Code session (v2.1.278) opened in the repo, question asked in natural words with the CSV path.

**Trigger.** Pass. The session loaded `mtg-archive:commander-swap` as its first action. Answer time 1m 32s.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Ranked candidates via retention, 5 of the service's top 20 | Pass | `POST /commander-alternatives` returned 20 candidates, all five-color, each retaining 99 of 99. Five presented. For a five-color deck retention ties, so the service order is `edhrec_rank` alone. |
| 2 | Retained/lost counts, every loss flagged, one ban citation resolved | Pass (shape) | Every candidate shows "keeps the whole deck". The real deck has zero losses. A probe with Mana Crypt added produced a `banned` loss citing ban event 98. `GET /ban-events/94` resolves the Golos ban to 2021-09-13 with the announcement URL. The color-identity loss path cannot fire for a five-color deck inside the top 20. |
| 3 | Original commander's ban cited to its dated event | **Fail** | The answer says Golos is banned with no ban footnote. The data exists: `GET /cards/{id}` legality rows carry ban event 94. SKILL.md never asks the agent to fetch or cite it, and the alternatives response's `original` block carries no ban citation. |
| 4 | Agent-side function re-rank, answer says which were re-ranked and why | Partial | The answer states the measure and names the 3 semantic-ranked candidates (First Sliver 0.382, Jodah 0.423, Esika 0.472) and the 17 ordered by reading. At semantic depth 200 only 3 of 20 shortlist cards receive a distance. SKILL.md calls the response array `hits`; the service returns `candidates`. |
| 5 | Collection scope stated | Pass | "Collection scope: false." |
| 6 | Answer contract: fetchable footnotes, versions block once | Pass | Six card footnotes with fetch paths. One versions block: banlist 2026-02-09. The 15 unpresented candidates are named without footnotes. |
| 7 | No model-recall facts | Pass with a caveat | All quoted Oracle text comes from `GET /cards`. The claim "Golos is the only banned card in it" carries no citation, and the collapsed transcript does not show its source. |
| 8 | Verdict recorded with transcript | This comment | Transcript attached below. |

### Other observations

- The CSV marks no commander. The agent inferred Golos from its banned status and stated the assumption. SKILL.md says a list without a commander is a request error and to ask the user.
- Semantic distances drift by about 0.002 between two runs of the same query on the same corpus (0.380 vs 0.382 for The First Sliver).
- Five-color cards inside the 200 semantic hits but outside the top-20 shortlist: Codie (semantic rank 21), Tazri, Beacon of Unity (119), Marina Vendrell (184). A functionally closer commander below the 20th five-color `edhrec_rank` never reaches the answer.
- The service accepted a 101-card decklist without complaint in the probe. Not in scope here.

### Follow-up issues

- commander-swap skill text: cite the old commander's ban event, `hits` -> `candidates`, no-commander-marker rule (criteria 3 and 4, and the inference note).
- semantic re-rank covers 3 of 20 shortlist candidates for a five-color deck (criterion 4). Design decision.
- semantic distance drift between identical queries.

No issue for the fixture note: a five-color commander cannot exercise color-identity losses, so a four-color banned commander would be the stronger fixture next time. No issue for the 101-card acceptance; out of scope here.

**Overall:** the flow works end to end with citations. Criterion 3 fails on skill text alone and the fix is one file. Criterion 4 is partial and needs a design decision. The acceptance record lives at `eval/acceptance/done-query-1-banned-commander.md`.

### XMage floors at cutover (2026-09-30)

Done-query 1 has no simulation benchmark of its own. Its deck is the parity deck `dq1-golos`; the parity gate's final run gives its XMage floors: `dq1-sol-ring-t2/goal-aware` 32.5% and `dq1-demonic-t2/goal-aware` 26.9% at 10,000 runs, seed 20260928 (`eval/parity/cutover/report.md`).
