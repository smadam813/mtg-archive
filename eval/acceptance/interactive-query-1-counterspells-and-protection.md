# Interactive-query 1 acceptance: counterspells and protection against turn-4 Pithing Needle

Verdict recorded 2026-10-11. This is a one-time acceptance record for the first tier-4 question (ADR 0013), not a standing done-query. The transcript is `interactive-query-1/transcript.md`.

**Fixture.** The done-query 2 final 99, `done-query-2/final-99.json`: Obeka, Brute Chronologist and 99 cards. The deck holds An Offer You Can't Refuse, Arcane Denial, Flusterstorm, Mana Drain and Pact of Negation.

**Environment.** Compose project `mtg-archive` with the GPU overlay. The `xmage` and `service` images were rebuilt from `main` before the run, because the old images predated two later changes. Engine `xmage-1.4.62V1+5e6c91b6`. Plugin `mtg-archive@mtg-archive` updated to the same content as `plugin/mtg-archive/`. Banlist 2026-02-09. The maintainer asked the question in a new Claude Code session (v2.1.295, Opus 5.5) in `C:\git\mtg-archive`, at 01:16 UTC, after both containers were healthy.

**Question.** "Here's my Obeka deck: eval/acceptance/done-query-2/final-99.json. It runs Mana Drain, Arcane Denial, Flusterstorm, Pact of Negation and An Offer You Can't Refuse. Against the usual counterspell-and-removal pressure, do my counterspells and protection actually keep a turn-4 Pithing Needle on track?"

### Calls

1. Skill `mtg-archive:deck-check`.
2. Read `references/answer-contract.md`, `references/default-disruption-profile.md` and the deck file.
3. `GET /healthz`: ok.
4. `POST /simulate`, abstracted: `interactive-query-1/request-abstracted.json`.
5. `POST /simulate`, interactive: `interactive-query-1/request-interactive.json`.
6. `POST /simulate`, interactive with `play_policy` `goal-aware-hold`: `interactive-query-1/request-hold.json`.

Each request has goal `on_battlefield` Pithing Needle by turn 4 and the default profile: counterspell turn 3, targeted removal turn 4. Each sends 1,000 pairs, seed 42 and `turn_horizon` 4. No request names a format, so the service used Commander. The agent then read the three envelopes with `jq`. It made no other service call.

### Result

| Run | Baseline | Disrupted | Delta |
|---|---|---|---|
| Abstracted, goal-aware-greedy | >= 67%, greedy policy (64.0–69.8%) | >= 0.4%, greedy policy (0.2–1.0%) | 0.666 |
| Interactive, goal-aware-greedy | same as above | >= 6.6%, greedy policy (5.2–8.3%) | 0.604 |
| Interactive, goal-aware-hold | >= 67.7%, greedy policy (64.7–70.5%) | >= 13.3%, greedy policy (11.3–15.5%) | 0.544 |

| Event | Abstracted fired | Interactive fired / answered / not cast | Hold fired / answered / not cast |
|---|---|---|---|
| Counterspell, turn 3 | 0.737 | 0.737 / 0.038 / 0 | 0.720 / 0.045 / 0 |
| Atomize, turn 4 | 0.775 | 0.788 / 0.062 / 0 | 0.796 / 0.128 / 0 |

Each run: 0 engine errors, `deck_verdict` legal. Response cards: An Offer You Can't Refuse, Arcane Denial, Counterspell, Delay, Dispel, Flusterstorm, Mana Drain, Pact of Negation, Pyroblast, Red Elemental Blast, Spell Pierce, Swan Song. `unprofiled_responses` is empty.

**Replay.** The same three requests with the default horizon of 10 return the same floors, intervals and deltas. The event rates are different, because the opponent then fires after turn 4 too (for example counterspell fired 0.904 and answered 0.085, interactive). The numbers in the answer agree with the session's own envelopes.

### Criteria

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Session in the repo, on the current plugin and the current sidecar image | Pass | Working directory `C:\git\mtg-archive`. The agent read the skill from the plugin cache. The envelopes report `xmage-1.4.62V1+5e6c91b6`, the image rebuilt from `main`. |
| 2 | Natural question about the final 99's counterspells and protection, turn-4 Pithing Needle, default profile | Pass | The question names no skill, tier, mode or profile. The agent sent the default profile and said so: "Assumed disruption (the default profile)". |
| 3 | deck-check fires | Pass | The first tool call is the Skill `mtg-archive:deck-check`. The maintainer confirmed it. |
| 4 | Abstracted and interactive on one seed, within the interactive cap | Pass | Both requests use seed 42 and 1,000 pairs. The cap is 10,000. The two baseline arms are equal. The agent also made the hold run, which was added after the plan was written. |
| 5 | Both disrupted floors with the floor label, both resilience deltas, per-event fired, answered and not-cast rates | Pass | The table gives each floor with ">= X%, greedy policy" and its 95% interval, and each delta. The event table gives fired, answered and not cast per event, with the abstracted fired rate beside them. The answer also says the gap mixes the real-card rules with the deck's answers. |
| 6 | Every interactive assumption line echoed, disruption cards named | Partial | Each of the 11 interactive lines and the 3 hold lines appears under its own heading. Counterspell ({U}{U}), Atomize ({2}{B}{G}) and Rout ({3}{W}{W}) are named with their costs. Most lines are shortened, not echoed in full. Dropped: the colors and card types of the disruption cards, the board-wipe targeting sentence, the per-card protection list and the phase-out rule in the greedy response rule, the commander, token and dies-trigger sentences in "Real rules", "scores no cast goal", "A card that is not cast spends nothing", "or a wipe" in the hold reserve, and the reason in the hold limits. |
| 7 | Record with the frame, the calls, the result, a verdict per criterion, and the transcript | This record | Transcript and the three requests are under `interactive-query-1/`. |

### Other observations

- **The lead misreads the answered rate.** The answer says the opponent's Counterspell "is answered in under 5% of the games where it is cast". `answered_rate` is a share of all runs: 0.038 of runs, which is 5.2% of the 737 runs where it was cast.
- **One footnote is not fetchable.** Footnote 2, "three simulate envelopes", has no fetch path. The envelopes carry only one citation, Obeka. Pithing Needle and the disruption cards carry none.
- **Two response cards are unprofiled cards.** Arcane Denial and Mana Drain are in `response_cards` and in `engine_coverage.unprofiled`, while `unprofiled_responses` is empty. The answer reports both lists as the envelope gives them. This is by design: the response-unprofiled flag is separate from the unprofiled flag (`GLOSSARY.md`). Both cards have a "counter target spell" response feature. Their main profile is unprofiled because of the delayed trigger: Mana Drain's later mana and Arcane Denial's later draws. A probe confirms that both cards answer. It used a deck where each card is the only answer, with a turn-1 counterspell, 1,000 pairs and seed 7. The answered rate was 0.003 for Arcane Denial and 0.012 for Mana Drain.
- **The answer explains the small effect.** It ties the result to three assumptions. The rule skips pacts and "unless its controller pays" counters. The counterspell fires right after the Needle is cast. The hold reserve starts only after the goal holds. The removal answered rate rises from 0.062 to 0.128 under the hold policy.

### Fixes in this PR

- **Criterion 6.** The deck-check tier-4 report and the answer contract now say to quote each assumption line word for word, with no merged, split or dropped clause. The disruption-cards line keeps each card's cost, colors and type. A line is quoted once for both runs only when the two blocks give the same words.
- **Answered rate.** The tier-4 report now says that each event rate is a share of all runs. The answer says "of all runs" next to `answered_rate`, and divides by `fired_rate` for the share of cast cards.
- **Run footnote.** The answer contract listed a `run` citation type with no endpoint, and deck-check said the envelope carries a run citation. No service response carries one, so the agent wrote an unfetchable footnote. The `run` type is gone from `CitationType`, `GLOSSARY.md`, `docs/api-conventions.md` and the contract. The contract now says to give the seed, run count and engine next to the floor, and to add no footnote for a run.
- `DeckCheckSkillTests` and `PluginScaffoldTests` pin these rules.

**Overall:** the first tier-4 question runs end to end from a natural question. deck-check fires, makes the abstracted, interactive and hold runs on one seed, and reports every floor, delta and event rate correctly. Criterion 6 is partial: the answer echoes every assumption line, but shortens most of them.
