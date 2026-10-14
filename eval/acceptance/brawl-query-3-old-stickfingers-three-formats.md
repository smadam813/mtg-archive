# Brawl-query 3 run: Old Stickfingers deck across the three Brawl formats

Run recorded 2026-10-06. This is a run record, not a verdict. The calls and result below come from a working session. The trigger was tested in a fresh session, as the user reports.

**Fixture.** An Old Stickfingers deck: commander plus 99, Black-Green, 100 cards.
The deck is `brawl-query-3/deck.txt`. Legality captures are in `brawl-query-3/legality.json`.
**Environment.** Compose stack up. Plugin `mtg-archive@mtg-archive`. Brawl legality as of 2026-10-05 (Scryfall).

**Question.** "Which Arena Brawl queues can I take this deck to, and what must change for each?" The agent invoked `mtg-archive:deck-check` and read the deck from the fixture file.

### Calls

1. `GET /healthz`: ok.
2. `POST /decklist-parse` with the deck text: no notes, 88 entries, commander Old Stickfingers.
3. `POST /deck-verdict` with `format` `brawl`.
4. `POST /deck-verdict` with `format` `competitive_brawl`.
5. `POST /deck-verdict` with `format` `standard_brawl`.

The agent made no `GET /cards/{id}` call and no simulate call.

### Result

| Format | Status | Deck size | Required | Findings |
|---|---|---|---|---|
| `brawl` | illegal | 100 | 100 | Demonic Tutor `banned`, role `main` |
| `competitive_brawl` | illegal | 100 | 100 | Old Stickfingers `banned`, role `commander` |
| `standard_brawl` | illegal | 100 | 60 | Commander and 66 of the 99 other cards `not_legal`; shape finding `too_many_cards`, rule 903.12 |

Each card finding carries a `legality` citation, for example "brawl banned as of 2026-10-05". The `versions` block of each response reads `format` and `legality_as_of` 2026-10-05T09:01:54.697+00:00. No response carried `name_rewrites`.

The service result agrees with `legality.json`: Old Stickfingers is `legal`, `banned`, `not_legal`, and Demonic Tutor is `banned`, `legal`, `not_legal`, in that format order. The fixture counts 88 names legal in Brawl, 88 in Competitive Brawl, and 22 in Standard Brawl, out of 89 names. That matches the findings: one ban each, and 67 `not_legal` names in Standard Brawl.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | All three formats read from the wording, one call each | Pass | "Arena Brawl queues" gave three `POST /deck-verdict` calls. |
| 2 | Format reading stated at the top | Pass | "Treating 'Arena Brawl queues' as all three Brawl formats, one call each." |
| 3 | One verdict per format with its status | Pass | Three `illegal` verdicts, one section each. |
| 4 | Each blocker cited with its `legality` citation | Partial | The footnotes cite Demonic Tutor, Old Stickfingers, rule 903.12, and Old Stickfingers in Standard Brawl. The other 66 `not_legal` cards share one footnote, not one footnote each. |
| 5 | Fix stated per format | Pass | Brawl: swap one card. Competitive Brawl: change the commander. Standard Brawl: rebuild to 60 cards. |
| 6 | Shape finding cited with its rule | Pass | `too_many_cards`, rule 903.12. |
| 7 | One merged versions block at the end | Pass | Formats and `legality_as_of` 2026-10-05. |
| 8 | No bracket claimed for Brawl | Pass | The answer says the service reports no bracket for Brawl. |
| 9 | Fresh-session trigger | Pass (user report) | The user ran the question in a fresh session. The session version, first action, and answer time are not recorded here. |

### Observations

- The Competitive Brawl fix is one change, but the answer named no replacement. It pointed to `commander-swap`. The agent did not run `commander-swap`, so no candidate is recorded here.
- The answer gave no card name for the Brawl swap. Any legal card works.
- The Standard Brawl count is cards, not names. All 66 `not_legal` main cards are singletons, so the two counts match.
- Perennial Gravewarden is a digital-only Alchemy card (`yecl`). It is `legal` in Brawl and Competitive Brawl, and `not_legal` in Standard Brawl.
- `next_bans_due` in the fixture is 2026-10-12. A ban update can change the Brawl and Competitive Brawl verdicts.

### Answer

Treating "Arena Brawl queues" as all three Brawl formats, one call each.

- Brawl: illegal. Cut Demonic Tutor (banned)[^1] and add one legal card.
- Competitive Brawl: illegal. Old Stickfingers is banned as commander[^2]. Change the commander. The other 99 cards pass.
- Standard Brawl: illegal. The deck has 100 cards and the format allows 60[^3]. The commander and 66 of the 99 other cards are not legal[^4]. Rebuild the deck.

Versions: formats brawl, competitive_brawl, standard_brawl; Brawl legality as of 2026-10-05.

[^1]: Demonic Tutor: "brawl banned as of 2026-10-05." `GET /cards/82004860-e589-4e38-8d61-8c0210e4ea39`.
[^2]: Old Stickfingers: "competitivebrawl banned as of 2026-10-05." `GET /cards/1d85eec2-80db-466c-bd48-d2a12866e8bd`.
[^3]: Rule 903.12. `GET /rules/903.12`.
[^4]: Old Stickfingers: "standardbrawl not_legal as of 2026-10-05." `GET /cards/1d85eec2-80db-466c-bd48-d2a12866e8bd`.
