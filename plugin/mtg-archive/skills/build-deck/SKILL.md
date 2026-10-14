---
name: build-deck
description: "Build a Commander or Arena Brawl deck from the user's collection toward a goal and a bracket. Also change a decklist that the user gives: upgrade, tune, or fix it, swap cards, or fix the manabase. Use this skill for every complaint about mana or one color, such as \"short on blue\", \"not enough blue mana\", \"color-screwed\", \"can't cast my spells\", or \"my UUU spells never come online\". Use this skill for a goal such as \"build me a deck that consistently drops X on turn 1\". Use this skill even when the user does not say \"build\", \"tune\", or \"deck\". A pasted decklist with a problem to solve belongs here."
---

Read `../../references/answer-contract.md` first. It states how to reach the service, the down-service rule, and how to report.

Format wording resolves through `../../references/phrase-to-format.md`; the Format section below says what changes per format. Power-level phrasing resolves through `../../references/phrase-to-bracket.md`. A resilience goal without specifics uses `../../references/default-disruption-profile.md`.

## What this skill is

This skill is the recommendation loop of ADR 0006. You drive the loop. The service grounds it: index queries, tier-1 math, bracket classification, and simulation. It holds no recommendation logic and no threshold. Never ask it to optimize, suggest cards, or fill a template.

The endpoints you compose, all with curl and JSON:

- `POST /cards/search`: structured filters under collection scope.
- `POST /cards/semantic-search`: the same filters plus a natural-language query.
- `POST /tier1-report`: exact mana and category math for one decklist.
- `POST /bracket-classification`: the bracket floor of one decklist against a target.
- `POST /simulate`: the tier-2 run on XMage, and tier 3 with a disruption profile.
- `POST /commander-alternatives`: owned commanders ranked by how many of the current main deck they keep.
- `POST /deck-verdict`: the deck verdict of one decklist in one format: card legality and deck shape, with no simulation.
- `POST /decklist-parse`: pasted deck text, such as an Arena export, turned into the decklist body.

The only expensive call is `POST /simulate`. Every other call is cheap.

## Loop parameters

These values are fixed. The answer states each one it uses.

| Parameter | Value |
|---|---|
| `iteration_budget` | 15 |
| `plateau_rounds` | 3 |
| `default_target` | 0.80 |
| `confidence_level` | 0.95 |

- `iteration_budget`: simulate calls per request. The user can raise it with an explicit number. The service never enforces it. A loop that tier 1 answers alone counts rounds against it instead.
- `plateau_rounds`: swap rounds in a row without a fitness rise before the loop stops. The fitness value is the lower bound, or the tier-1 probability when tier 1 answers alone.
- `default_target`: the target when the user gives no word and no number.
- `confidence_level`: the interval the lower bound comes from.

## Format

Pick the format first, before step 1. Read it from the user's words with `phrase-to-format.md`. State the reading at the top of the answer. The steps hold in every format. This section lists each rule that changes in a Brawl format (`brawl`, `competitive_brawl`, or `standard_brawl`):

- Send `format` on every call that takes it: `POST /cards/search`, `POST /cards/semantic-search`, `POST /tier1-report`, `POST /simulate`, `POST /commander-alternatives`, and `POST /deck-verdict`. Each defaults to `commander`. A Brawl format takes no `banlist_version`.
- There is no bracket step. Skip the bracket in step 1, the bracket call in "Deck tune", the bracket classification in step 5, and the bracket floor in step 9. `POST /bracket-classification` refuses a Brawl format with a `400`.
- Power words set no target. State: "No power target: Arena publishes no bracket system for Brawl." The one exception: "Competitive Brawl" or "ranked Brawl" picks the `competitive_brawl` format.
- The main deck is 99 cards, or 59 in Standard Brawl. Ramp, draw, and removal counts are your judgment, not a bracket's.
- The cost ladder is the tier-1 screen, then the reach gate, then tier 2.
- The bound decklist holds every owned, legal, on-identity route card, with no Game Changer cap and no bracket check. Read L as `mulligan_looks` from `POST /tier1-report` sent with the format. The format's free mulligans set it.
- Rank the commander shortlist by `popularity_rank` in the format, as step 2 says. Disclose the popularity source by `versions.popularity_source`, not by the format.
- An Arena export goes through `POST /decklist-parse` first, as "Deck tune" says. An export with no format word means ask. An export with no `Commander` section means ask which card is the commander.
- Name the game in the answer, next to the floor: 25 starting life, the free mulligan count, and no draw on turn 1 on the play. Read the mulligan count and the on-the-play line from the envelope's `simplifications`. A mana goal has no envelope: read the on-the-play line from the tier-1 `assumptions`, and read the free mulligans from `mulligan_looks` (4 means one free mulligan, 3 means none). State 25 life as the Brawl rule, not as a service result.
- The versions block leaves out the bracket spec and the Game Changers snapshot, because no bracket call ran. It carries `format` and `legality_as_of`.

## Step 1: Resolve the request to numbers

Do this before the first service call. Record each result. The answer states them first.

**Bracket.** Resolve the power-level phrase with `phrase-to-bracket.md`. An unmatched phrase means ask. Record the phrase and the bracket. A deck tune with no phrase takes its bracket from the supplied decklist; see "Deck tune".

**Target.** The target is a number. It is the confidence-interval lower bound the goal probability must reach. Resolve the user's word with this table:

| The user says | Target |
|---|---|
| "every game", "always" | 0.95 |
| "consistently", "reliably" | 0.80 |
| "usually", "most games" | 0.60 |
| "often" | 0.40 |

A number from the user wins over the table. No word and no number means `default_target`. State that choice as an assumption. Record the word and the target.

**Budget.** The budget is `iteration_budget` unless the user raises it. Every simulate call counts: a fitness call, the confirmation call, and a tier-3 call. A loop that tier 1 answers alone counts rounds instead. There are no mid-loop check-ins. Run the loop to a stop. Then answer once.

**Goal.** Translate the goal into the closed goal-predicate vocabulary:

- `on_battlefield(card, by_turn)`: the card is on the battlefield at the end of that turn.
- `cast(card, by_turn)`: the card resolved by that turn. A countered cast does not count.
- `all_of([...])`: every member holds.

The wire form is `{ "kind": "on_battlefield", "card": "<exact Oracle name>", "by_turn": 4 }` or `{ "kind": "all_of", "goals": [...] }`. The card must be in the decklist you send.

A goal on a multi-face card (modal double-faced, transform, split, adventure) names the card one of three ways:

- The full card name, as the decklist entry carries it, matches either face: `Esika, God of the Tree // The Prismatic Bridge`.
- The front face name matches the front face only: `Esika, God of the Tree`.
- The back face name of a modal double-faced card matches that face only: `The Prismatic Bridge`. The engine then casts the back face for its own cost, or plays a land back face as the land drop.

A goal on any other face cannot run: a transform back face, the second half of a split or adventure card, a meld or flip face. Treat it as a goal outside the vocabulary: the front-face goal is the nearest predicate.

An MDFC land has a spell front face and a land back face: `Shatterskull Smashing // Shatterskull, the Hammer Pass`. The engine plays it as a land when no other land is in hand. It never does so while the goal wants the front face. A pathway enters as the face that makes a color the goal needs, and as its front face on a tie. The mulligan and the land count treat an MDFC land as a land.

If the goal does not fit the vocabulary, do not substitute in silence. Tell the user what the vocabulary cannot express. Name the nearest expressible goal. Build toward that goal. State the substitution in the answer, next to the probability. Examples:

- "Win by turn 6" has no predicate. The nearest is `on_battlefield` for the win-condition card by turn 6.
- "Have a board of five creatures" has no predicate. The nearest is `all_of` over the named creatures.
- "Draw a card every turn" has no predicate. Say so. Offer a draw row from a tier-1 category instead.

A goal about mana or card categories alone needs no simulate call. The tier-1 report answers it exactly. Its probability is the fitness value. The target applies to it directly. It needs no confidence interval and no confirmation call. A goal that names a card or a sequence needs tier 2.

**Mana goal.** A goal stated as mana is a mana goal: "cast my UUU spells on time", "fix the manabase", "short on blue". The color the user names is the goal color. Ask for the color when the user names none.

- The heavy-pip rows of a color are its `demand_vs_supply` rows with 3 or more `pips`. If no row has 3, they are the rows with 2 or more.
- The goal rows are the heavy-pip rows of the goal color.
- Each row's `turn` is its card's mana value. Read `casting_probability` at that turn.
- The fitness value is the lowest `casting_probability` among the goal rows.
- The target comes from the word table above. No word means `default_target`. State that choice as an assumption.
- Check the goal rows on the baseline tier-1 report of step 5. If they meet the target and the goal color has a row with 2 or more `pips` that is not a goal row, the goal rows become all the goal color's rows with 2 or more `pips`. State the widening in the declared frame.
- If the goal rows, after any widening, meet the target at baseline, stop with no swap. Report the baseline table. Count 0 rounds spent. The "Stopped on" line is "Stopped on: target met at baseline; no swap."
- If the widened goal rows miss the target, run the loop as usual on them.

A mana goal spends no simulate call and no confirmation call. It reports no confidence interval and no reach bound.

**Resilience.** If the user asks for resilience, resolve the disruption profile with `default-disruption-profile.md`. Send it on every simulate call. Each call then plays two games per run. It still counts once against the budget.

## Deck tune: start from the user's decklist

Use this entry when the user supplies a decklist and asks to change it: upgrade, tune, fix, or swap cards. A deck tune accepts any goal. A goal that names a card or a sequence still uses tier-2 fitness.

- Build the decklist body from the supplied decklist the way `deck-check` does. Send pasted deck text through `POST /decklist-parse` first, as its Step 1 says.
- On an `unresolved_names` problem, show the names. Ask for the exact spelling. Never guess a card.
- Do not check that the user owns the supplied cards.
- Skip step 2 and step 4. The supplied commander stays. Never revisit it.
- Take every swap-in from a step-3 query under collection scope. After a swap, the decklist holds no more copies of a swap-in than its `available`.
- If the user names no bracket, send the supplied decklist to `POST /bracket-classification` once. Any `target_bracket` gives the same floor. Use its `bracket_floor.bracket` as the target bracket. State that choice as an assumption.
- Start the loop at step 5 with the supplied decklist. This decklist is the baseline.

## Step 2: Shortlist commanders (the outer choice)

A deck tune skips this step.

The commander is the outer choice. Pick it once. Revisit it only in step 7.

Set `MTG_ARCHIVE_URL` as `answer-contract.md` states before the first call.

If the user names a commander, confirm the user owns it:

```sh
curl -sS "$MTG_ARCHIVE_URL/cards/search" -H 'content-type: application/json' -d '{
  "name": "Kaalia of the Vast", "role": "commander", "collection_scope": true, "limit": 5
}'
```

An empty `candidates` list means the collection lacks a commander-legal copy. Say so and stop.

Otherwise, shortlist owned commanders whose colors serve the goal. The boundary returns only cards legal in the commander role. You never check that yourself:

```sh
curl -sS "$MTG_ARCHIVE_URL/cards/semantic-search" -H 'content-type: application/json' -d '{
  "query": "commander that reanimates creatures from the graveyard",
  "role": "commander", "collection_scope": true, "k": 30
}'
```

Add `"types": ["Legendary", "Creature"]` or `"tags": [...]` to narrow. Rank the shortlist by fit to the goal, then by `popularity_rank` (a `role` of `commander` reads the commander popularity order). State the popularity source as `answer-contract.md` says. Take the first. Step 7 revisits the choice with the built decklist in hand.

## Step 3: Generate candidates under collection scope

Every candidate query carries `"collection_scope": true`, `"role": "main"`, and the commander's identity letters in `"color_identity"`. Under scope each hit carries `available` = min(copy limit, owned quantity). Never put more copies of a card in the decklist than `available`. This step needs no new index capability. Structured filters, Oracle Tag filters, and card search compose it.

Run one query per role in the decklist. Typical roles and their filters:

| Role | Query |
|---|---|
| Goal pieces | `semantic-search` with the goal in words; `k` 50 |
| Enablers of the goal | `semantic-search` with what the goal piece needs; `k` 50 |
| Ramp | `search` with `"tags": ["ramp"]`, `"max_mana_value": 3` |
| Card draw | `semantic-search` with "draw cards" |
| Removal | `search` with `"tags": ["removal"]` |
| Untapped lands | `search` with `"types": ["Land"]`, `"enters_tapped": "untapped"` |
| Other lands | `search` with `"types": ["Land"]` |

Example:

```sh
curl -sS "$MTG_ARCHIVE_URL/cards/search" -H 'content-type: application/json' -d '{
  "tags": ["ramp"], "max_mana_value": 3,
  "color_identity": ["W", "B", "R"], "role": "main", "collection_scope": true, "limit": 100
}'
```

The Oracle Tag names are the ones `GET /cards/{id}` lists under `tags`. `ramp` and `removal` are two. When you are unsure of a tag name, use `semantic-search` instead.

Page with `offset` when a role needs more than one page. If `semantic.enabled` is false, report `semantic.disabled_reason`. Then use structured filters for that role.

Keep every hit's `card` citation. The decklist and the answer use them.

## Step 4: Assemble the decklist

A deck tune skips this step.

Assembly is your judgment. The service does not do it. Build a main deck of the format's size (99 in Commander; see "Format") that serves the goal:

- The goal pieces and their enablers first. Every card the goal names must be in the decklist.
- Then ramp, draw, removal, and interaction in counts that fit the bracket.
- Then lands. Start near 36 to 38. Let the tier-1 report correct the count.
- Respect `available` for every card. Respect singleton: one copy of a nonbasic card.
- Keep only cards inside the commander's color identity. The boundary already ensured this for every hit.

The decklist wire form is the same on every endpoint:

```json
{
  "commanders": [{ "name": "Kaalia of the Vast" }],
  "entries": [
    { "card": { "name": "Sol Ring" }, "quantity": 1 },
    { "card": { "name": "Plains" }, "quantity": 6 }
  ]
}
```

Use exact Oracle names or `oracle_id`. An `unresolved_names` problem lists the references that did not resolve. Fix them from the hit citations and resend.

## Step 5: Gate through the cost ladder

Caution: a decklist that fails either check below spends no simulate call. It goes back to step 4. In a deck tune, it goes back to the swap.

**Tier-1 screen.** Send the decklist with categories for the goal pieces, ramp, and lands:

```sh
curl -sS "$MTG_ARCHIVE_URL/tier1-report" -H 'content-type: application/json' -d '{
  "decklist": { ... },
  "categories": [
    { "name": "goal", "cards": [{ "name": "Avacyn, Angel of Hope" }], "k": 1 },
    { "name": "ramp", "cards": [ ... ], "k": 1 }
  ],
  "max_turn": 6
}'
```

Read `demand_vs_supply`. Each row is one card and one color. On each goal-piece row, `supply` must reach `pips`. `casting_probability` must sit near the target.

Read `land_drops` at the goal turn. Adjust the land count.

**Other colors (mana goal).** Read the heavy-pip rows of each other color in the commander's identity. A candidate fails the screen when one of these rows has a lower `casting_probability` than in the baseline. Compare against the baseline, never against the current best. Record the color. The answer table still shows the candidate and marks the drop.

**Reach bound.** A mana goal skips the reach bound and the reach gate. The reach bound is an upper bound on the goal probability. Tier 1 alone gives no such bound, because tier 1 assumes no mulligans and the engine mulligans. Only the reach bound can show that a target is out of reach.

Caution: a goal piece in the command zone has no reach bound. Never declare such a goal out of reach. The same holds when the commander draws, selects, or tutors, because that route is outside the library.

Compute the reach bound of one decklist as follows:

- The route category holds every card in the decklist that can move the goal card out of the library by the goal turn. It includes copies of the goal card, tutors that can find it, and draw or card-selection cards.
- It also holds every card that searches, shuffles, or mills the library, such as a fetch land or a land tutor. Such a card changes which cards the draws reach.
- Count each route card as if it always succeeds. Leave out a card only when no game can resolve it by the goal turn. If you are not sure, count it. The overcount keeps the bound sound.
- Send the decklist to `POST /tier1-report` with the route category at `k` 1. `q` is the route category's `category_draws` probability at the goal turn.
- The reach bound is `1 - (1 - q)^L`. `L` is the most seven-card hands the run's mulligan policy can look at.
- `L` is `mulligan_looks` from the same tier-1 report. The format's free mulligans set it, so never assume a number. Each mulligan reshuffles the library, so each hand is a fresh draw.
- For an `all_of` goal, compute the reach bound of each member that starts in the library. Use the smallest.

**Out-of-reach declaration.** Before the first simulate call, compute the reach bound of the bound decklist. The bound decklist is the current decklist with every owned route card swapped in, each up to its `available` count. Swap out cards that are not routes, so the decklist keeps its main-deck size. Find the route cards with the step-3 queries under collection scope. No decklist from the collection has more routes, so this reach bound holds for every candidate.

Caution: many tutors and draw engines are Game Changers. The bound decklist holds Game Changer routes only up to the target bracket's Game Changer limit. The limit is none in brackets 1 and 2 and three in bracket 3. `q` depends only on the number of route cards, so any choice of Game Changer routes gives the same bound. The bound decklist must pass bracket classification. Otherwise the reach gate can fail every decklist that passes the bracket check.

If this reach bound is below the target, declare the target out of reach. Declare it once. Record the reach bound, `q`, `L`, and the route cards. The answer states them.

Example: done-query 2 has `q` 0.35, `mulligan_looks` 4, and a target of 0.80. The reach bound is `1 - 0.65^4`, about 0.82. The bound is above the target, so the rule does not declare the target out of reach.

**Reach gate.** A decklist whose own reach bound is below the target fails the gate. Add route cards to it. After an out-of-reach declaration, this check no longer applies. The pip and bracket checks still apply.

**Bracket classification.** Send the decklist with the numeric target bracket. The service never sees the phrase:

```sh
curl -sS "$MTG_ARCHIVE_URL/bracket-classification" -H 'content-type: application/json' -d '{
  "decklist": { ... }, "target_bracket": 4
}'
```

The gate passes when `bracket_floor.within_target` is true. When it is false, `reasons` names the barometer and the cards that fired it. Cut or swap those cards. Re-gate. Keep the `citations` for the answer.

Only a decklist that passes both checks reaches step 6.

**Mana goal loop.** A mana goal skips step 6 and step 8. The tier-1 report of each candidate gives its fitness value. Swap and count rounds as step 6 says. A round can change up to eight cards, because a manabase swap moves lands in sets. Aim each swap at the goal rows: more sources of the goal color, fewer sources that make no goal color.

Keep a candidate only if it passes both checks and its fitness value rises. Record every candidate you try, kept or not. The answer table needs them all.

## Step 6: Evaluate with tier 2 (the inner loop)

Pick one base seed for the whole request before the first simulate call. Record it. Every fitness call sends that seed in the existing `seed` field. The cards two decklists share then keep their relative opening library order, whatever the entry order. A later shuffle (mulligan, fetch, search) matches the two decklists only in part.

```sh
curl -sS "$MTG_ARCHIVE_URL/simulate" -H 'content-type: application/json' -d '{
  "decklist": { ... },
  "goal": { "kind": "on_battlefield", "card": "Kaalia of the Vast", "by_turn": 4 },
  "runs": 1000, "seed": 20260919, "turn_horizon": 6
}'
```

Set `turn_horizon` at or above the goal's last turn. Count the call against the budget.

Read from the result envelope:

- `probability.confidence_interval.lower`: the lower bound. The loop compares decklists on this number only.
- `probability.label`, `probability.confidence_interval`, `runs`, `base_seed`: the four values the floor label needs.
- `engine` and `engine_coverage`: the engine that played the runs, and the cards it does not fully cover. Name `engine.name` and `engine.version` in the assumptions of every floor. Floors from different engines or versions do not compare. A card in `engine_coverage.unimplemented` is a blank card: it holds its library slot and never casts. A card in `engine_coverage.unprofiled` runs in full, but the pilot reads only the features its profile can prove, so it may play the card late. If a goal piece or an enabler is on either list, the lower bound understates the decklist. State it first in the answer.
- `engine_errors`: the games that ended in an engine error. Each counts as a failure. Report the count when it is above 0.
- `deck_verdict`: the deck verdict, the same as `POST /deck-verdict` returns for the list and the format. `card_findings` lists each card that is not legal in its role, with its citation. Replace those cards before any further spend. A `status` of `unknown` means the legality rows are missing; state it in the assumptions.
- `curve` and `exemplars`: the first failure line tells you what the decklist lacked. Read it before you choose the next swap.

**Swap and repeat.** Change one to five cards per round. Aim each swap at the failure the exemplars and the tier-1 rows show: a missing color, a late enabler, too few lands. Re-gate through step 5.

Then send the new decklist with the same base seed. Keep it only if its lower bound rises. Count a round with no rise. Reset that count on a rise.

## Step 7: Stop

The loop has four stopping criteria. It stops at the first of (a), (b), and (c). Criterion (d) is a declaration, and a later (b) or (c) ends the loop:

- **(a) Target met.** The fitness value of the current best reaches the target. Go to step 8. A mana goal goes to step 9. A mana goal that meets the target at baseline, after the widening of step 1, makes no swap.
- **(b) Plateau.** `plateau_rounds` swap rounds in a row without a fitness rise.
- **(c) Budget exhausted.** The next simulate call would exceed the budget. A loop that tier 1 answers alone, such as a mana goal, spends no simulate call. It stops after `iteration_budget` rounds instead. Count rounds, not calls. The answer states this budget.
- **(d) Target out of reach.** Step 5 declares that the reach bound is below the target. This does not end the loop. Keep the base seed. Keep swapping to raise the lower bound. The loop ends on plateau or budget exhausted. It never reaches (a), so it spends no confirmation call.

In a deck tune, never revisit the commander. On (b) or (c), report as the last paragraph of this step says. Also name `commander-swap` as the next step.

On (b) with budget left, revisit the commander once. Do this only on a plateau below target, never on every round. Ask the service which owned commander keeps the most of the current decklist:

```sh
curl -sS "$MTG_ARCHIVE_URL/commander-alternatives" -H 'content-type: application/json' -d '{
  "decklist": { ... }, "collection_scope": true, "limit": 20
}'
```

Each entry in `candidates` carries `retained_count`, `lost_count`, and the `retained` and `lost` cards with a reason for each loss. The service ranks by `retained_count`. You re-rank the top entries by fit to the goal. Take the first that serves the goal. If none does, or the budget is gone, stop.

Keep the new commander's `retained` cards. Refill the `lost` slots from step 3. Keep the base seed. Reset the plateau count.

A commander revisit changes the route cards. After a revisit, repeat the out-of-reach declaration of step 5 for the new commander, before the next simulate call. If the new reach bound is below the target, declare the target out of reach. If it reaches the target, withdraw any earlier declaration. Then (a) and step 8 apply again. Continue the inner loop.

On (b) and (c) report best-so-far. State the gap: the target, the best lower bound, and the difference. On (d), also state the reach bound, its difference from the target, and its difference from the best lower bound.

## Step 8: Confirm

A mana goal skips this step.

A target-meeting decklist is not a success until a confirmation call agrees. Send the same decklist and goal with a fresh seed. The fresh seed differs from the base seed. The confirmation call counts against the budget. If the budget cannot afford it, report best-so-far under criterion (c). Say the decklist is unconfirmed.

- Confirmed: the fresh-seed lower bound reaches the target. Declare success. Report both calls.
- Not confirmed: the base-seed result depended on its shuffles. Return to step 6 with the budget that remains. If none remains, report best-so-far with both numbers.

## Step 9: Report

**Deck verdict gate.** Before you answer, send the final decklist to `POST /deck-verdict` with the format. If the last `/simulate` call sent this same list in this format, its `deck_verdict` is that verdict; make no second call.

- Fix each break before you answer. A break is any entry in `card_findings`, and any entry in `shape_findings` other than `too_few_cards` and `no_commander`. Take each replacement from a step-3 query under collection scope. A fix changes the list, so return to step 5 with the budget that remains.
- A `status` of `incomplete` means the collection cannot fill the deck. Answer with the `incomplete` verdict and the number of missing cards: `required_deck_size` minus `deck_size`.
- Never pad the list with cards the user does not own.

Follow `answer-contract.md`. Put the declared frame first, before any number:

```text
Goal: Kaalia of the Vast on the battlefield by turn 4 (on_battlefield).
Target: lower bound ≥ 0.80 ("consistently").
Target bracket: 4 (treating "competition-ready" as bracket 4).
Budget: 15 simulate calls; 11 spent.
Stopping criteria: target met, plateau after 3 rounds without a rise, budget exhausted, or target out of reach (the reach bound is below the target).
Base seed: 20260919. Confirmation seed: 8675309.
Stopped on: target met, confirmed.
```

On an out-of-reach target, the stop line names the declaration and the stop that ends the loop. There is no confirmation seed:

```text
Stopped on: target out of reach (reach bound 0.53, q 0.17, L 4); then plateau after 3 rounds without a rise.
```

Caution: never state a point estimate anywhere in the answer, the lead included. A tier-2 probability appears only as `probability.label` next to its interval. Copy the label exactly as the service returns it: `>= X%, greedy policy`. Never shorten it, in prose or in a table row.

Then, in this order:

1. Goal-critical cards in `engine_coverage`, if any: each card and its list, `unimplemented` or `unprofiled`.
2. The decklist: commander, then the main deck grouped by role. Every card in the decklist carries a card footnote: the label and `GET /cards/{id}`. Take the id from the `card` citation the service returned: a search hit or a `retained` entry. This covers every entry: each nonbasic card, each basic land, and each card the user supplied. A card with no citation yet gets one from `POST /cards/search` by exact name before you answer. Send a card name that holds a non-ASCII character, such as "Thrór's Map", as UTF-8 JSON from a file, because an inline shell string can lose the character and the search then returns nothing. If the lookup still returns nothing, write "no citation returned" next to that card. Never cite only the commander. Never write that cards "resolve by exact name" in place of a footnote.

   Print every card footnote, however long the decklist is. Never offer the footnotes or the ids in place of printing them. A bare `[n]` that the user cannot fetch is not a citation.
3. The result envelope for the best call and the confirmation call: `probability.label`, `probability.confidence_interval`, `runs`, and `base_seed`, and the engine that played them: `engine.name` and `engine.version`. Under a disruption profile, state the profile, both arms, and the resilience delta.
4. The policies, by name: the play policy from `policies.play` and the mulligan policy from `policies.mulligan`. Name the game, if the Format section says to.
5. Up to 3 exemplar lines from `exemplars` of the best call. Give each line its `kind`, `seed`, `run_index`, and met turn or failure. The `seed` and `run_index` pair replays the line.
6. The gap, on plateau or budget exhaustion. On an out-of-reach target, the gap states the target, the reach bound, the best lower bound, and both differences. Name the route cards behind the reach bound.
7. The bracket floor. Never state a single bracket without the word "floor". Build the line from `bracket_floor`: "Bracket floor: {bracket} (target {target_bracket}, within target: {within_target})". List each barometer in `reasons` with its cards. Add the snapshot footnotes from `citations`.
8. The tier-1 assumptions block, once.
9. The versions block, merged across every response.

Every number sits next to the assumption that shapes it. Every card sits next to a citation the service returned.

### Mana-goal answer

A mana-goal answer opens with the same declared frame. It counts rounds, not simulate calls, and has no seeds:

```text
Goal: cast the blue heavy-pip spells on time (mana goal; fitness: lowest casting probability among the goal rows).
Target: fitness ≥ 0.80 (no word given; default target).
Target bracket: 3 (the supplied decklist's bracket floor; no bracket named).
Budget: 15 rounds, tier 1 only; 4 spent.
Stopped on: plateau after 3 rounds without a rise.
```

When the goal rows widen, the goal line states it:

```text
Goal: cast the blue heavy-pip spells on time (mana goal; goal rows widened to 2 or more pips; fitness: lowest casting probability among the goal rows).
```

When the goal rows meet the target at baseline, the Budget line counts 0 rounds spent, and the "Stopped on" line is:

```text
Stopped on: target met at baseline; no swap.
```

Always print the "Stopped on" line and the list of excluded cards under the table, even when the loop makes no swap.

Then render one table:

- Rows: for each color in the identity, its `by_color` line, then its heavy-pip rows. For the goal color, show its goal rows, widened or not. The `by_color` line shows the count, then mono, dual, and 3+.
- Each heavy-pip row and goal row shows its `casting_probability` at its turn.
- Columns: the baseline first, then every candidate the loop tried, in order. Above each candidate column, state the swap: cards out, cards in. Mark each candidate accepted or rejected. A rejected candidate names the color whose row dropped. Mark each dropped cell.
- The last row is the fitness value of each column.

Under the table, name each excluded card and its `reason` from each column's `mana_sources.excluded`. If a candidate's swap adds an excluded card, say that the card does not count toward `by_color`.

Name the game next to the table, if the Format section says to.

Put the tier-1 assumptions block next to the table. A tier-1 card name takes no footnote. The versions block holds only what the bracket calls returned. The bracket floor line and the gap follow the list above.

### Worked example

This table comes from the done-query 3 fixture (`eval/acceptance/done-query-3/`): Sen Triplets, goal color blue. The fixture builds both swaps from the supplied decklist, so this table shows each one against the baseline. In a live loop, a swap after an accepted swap starts from the current best.

- Swap a, accepted. Out: 2 Plains, 2 Swamp, Scoured Barrens, Orzhov Basilica. In: Hallowed Fountain, Watery Grave, Tundra, Underground Sea, Flooded Strand, Polluted Delta.
- Swap d, rejected on white. Out: 3 Plains. In: 3 Island.

| Row | Baseline | Swap a (accepted) | Swap d (rejected: W) |
|---|---|---|---|
| W sources (mono / dual / 3+) | 22 (8 / 6 / 8) | 22 (6 / 6 / 10) | 19 (5 / 6 / 8) ▼ |
| Supreme Verdict, WW, turn 4 | 72.9% | 72.9% | 63.6% ▼ |
| Wrath of God, WW, turn 4 | 72.9% | 72.9% | 63.6% ▼ |
| Austere Command, WW, turn 6 | 84.0% | 84.0% | 76.4% ▼ |
| Sun Titan, WW, turn 6 | 84.0% | 84.0% | 76.4% ▼ |
| U sources (mono / dual / 3+) | 21 (9 / 4 / 8) | 27 (9 / 8 / 10) | 24 (12 / 4 / 8) |
| Blue Sun's Zenith, UUU, turn 3 | 32.5% | 51.8% | 42.2% |
| Cryptic Command, UUU, turn 4 | 38.9% | 59.3% | 49.4% |
| Mass Manipulation, UUUU, turn 4 | 15.4% | 31.6% | 23.0% |
| Arcanis the Omnipotent, UUU, turn 6 | 55.3% | 75.0% | 66.0% |
| Mind Over Matter, UUUU, turn 6 | 28.2% | 49.6% | 38.8% |
| Spelljack, UUU, turn 6 | 55.3% | 75.0% | 66.0% |
| Omniscience, UUU, turn 10 | 75.7% | 90.4% | 84.3% |
| B sources (mono / dual / 3+) | 22 (8 / 6 / 8) | 22 (6 / 6 / 10) | 22 (8 / 6 / 8) |
| Necropotence, BBB, turn 3 | 35.7% | 35.7% | 35.7% |
| Fitness (lowest goal row) | 15.4% | 31.6% | 23.0% |

White has no 3-pip row, so its 2-pip rows apply. Swap a raises every blue row and leaves white and black unchanged. Swap d raises blue less and drops every white row, so it fails the screen.

## Fixed rules

- A down service means "start the compose stack". Never build a decklist from recall.
- The loop lives here. Never call or ask for a service-side optimizer.
- Never pass the user's phrase to the service. Resolve it here. Declare the mapping.
