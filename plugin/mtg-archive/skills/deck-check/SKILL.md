---
name: deck-check
description: "Analyze a Commander or Arena Brawl deck that the user already has, read-only. Use this skill when the user asks how the deck performs. Questions include the sources per color, the land drops, the card categories, the bracket, the consistency, the resilience, and which formats or Brawl queues the deck is legal in, and what blocks each. Use this skill for \"why is my deck short on blue?\" only when the user asks for the cause and no change. Do not use this skill for a request to change, fix, or swap anything, such as \"fix the manabase\" or \"I can't cast my blue spells\". Send those requests to build-deck."
---

Read `../../references/answer-contract.md` first. It states how to reach the service and how to report. Every answer comes from a service response. If the service is down, follow the contract's down-service rule and stop.

Read `../../references/phrase-to-format.md` to turn the user's wording into a format. The Format section below lists what changes in this skill.

## Trigger

Use this skill for every read-only question about a deck the user already has:

- Mana: sources per color, pip demand against supply, land drops, "why am I short on blue".
- Card categories: "how often do I see a ramp piece by turn 3".
- Bracket: "what bracket is this deck", "is this deck casual".
- Consistency: "how often do I have X on the battlefield by turn N".
- Resilience: "how does this deck hold up against interaction".
- Its own answers: "do my counterspells and protection keep my combo safe".
- Deck verdict: "is this deck legal", "which formats can this deck go to", "what blocks it".

Do not change the deck. A request to build, upgrade, tune, swap cards, or fix the manabase is `build-deck`. So is a complaint with no question, such as "I can't cast my blue spells" or "I keep getting color-screwed". A request to replace the commander is `commander-swap`. A fact question about one card or rule is `mtg-lookup`.

## Step 1: build the decklist

The deck routes take the same `decklist` body. Name the commander under `commanders`, and a companion under `companion`. List the other cards under `entries` with their quantity. A card is an exact Oracle name or an `oracle_id`.

```json
{
  "decklist": {
    "commanders": [{ "name": "Atraxa, Praetors' Voice" }],
    "entries": [
      { "card": { "name": "Sol Ring" }, "quantity": 1 },
      { "card": { "name": "Island" }, "quantity": 8 }
    ]
  }
}
```

Pasted deck text (an Arena export or a `<qty> <name>` list): send it as `text` to `POST /decklist-parse`, show the user the parsed list and its `notes`, then send its `decklist`; an export with no format word means ask (see `phrase-to-format.md`).

The service accepts a partial list. An unresolved name returns a problem with `unresolved_names`. Show the user those names and ask for the exact spelling. Do not guess a card.

## Step 2: pick the cheapest tier

The cost ladder is the reading order. Read down it and stop at the first tier that answers the question.

1. **Tier 1** answers every mana and card-category question. It is exact and cheap. It makes no simulate call.
2. **Bracket** answers every bracket question. It is cheap.
3. **Tier 2** is required when the goal names card semantics or sequencing: "cast X by turn N", "X on the battlefield by turn N".
4. **Tier 3** is tier 2 under disruption. Use it only for a resilience question.
5. **Tier 4** is interactive disruption: a scripted opponent casts each disruption event as a real card, and the deck can answer it. Use it when the user asks whether the deck's own counterspells, protection or interaction help. It costs more per pair than tier 3.

A question that mixes tiers composes several calls. Each call still starts at the cheapest tier that answers its part.

## Tier 1: `POST /tier1-report`

The request is the decklist, an optional `categories` list, an optional `max_turn` (default 10), and an optional `format` (default `commander`). The format's game rules set the cards seen by each turn: a Brawl format is on the play, with no turn-1 draw.

```json
{
  "decklist": { "...": "..." },
  "categories": [
    { "name": "ramp", "cards": [{ "name": "Sol Ring" }, { "name": "Arcane Signet" }] },
    { "name": "removal", "cards": [{ "name": "Swords to Plowshares" }], "k": 2 }
  ],
  "max_turn": 6
}
```

The service never invents a category. You supply each one as `{ name, cards }`. Build the membership in two passes:

1. **Oracle Tags first.** Call `POST /cards/search` with `tags` set to the tag, `color_identity` set to the deck's identity, and `limit` 200. When the user names a category in slang ("board wipes", "aristocrats"), take the tags from `../../references/archetype-glossary.md`. Make one call per tag. Say which tags you used. A tag alias ("boardwipe") resolves to its label on the service. Page with `offset` until the page is short. Keep the hits whose name is in the decklist. A deck card the search omits may still carry the tag. Check it with `GET /cards?name=` and read its `tags`.
2. **Agent judgment second.** Add or drop deck cards where the tag misses the user's meaning. Mark each of these as your own call.

`k` is the draw count the rows measure (default 1: at least one). Set it when the user asks for two or more.

Always echo the chosen membership in the answer. List every card in each category. Say which cards came from a tag and which from your judgment. The report's `input.categories` block confirms what the service resolved. Report that, not your draft.

The report has these parts. Report the ones the question asks for:

- `category_draws`: rows of (category, k, turn, probability). A row is P(at least k of the set among the cards seen by that turn).
- `mana_sources`: every source with its colors, and `by_color` counts split into mono, dual, and three-plus sources. `by_color` has a row for each color of the deck's color identity and for colorless only.
- `mana_sources.excluded`: the nonland cards that would make a color of the deck but do not count as sources, each with `name`, `quantity`, and `reason`. The reason `opponent_dependent` marks a nonland card whose every mana ability and fetch ability depends on another player or on combat. Tier 1 has no opponents. The reason `token_to_another_player` marks a nonland card whose every mana token goes to another player, such as An Offer You Can't Refuse.
- `demand_vs_supply`: one row per spell per color: pips demanded, the turn, the sources available by that turn, and the casting probability.
- `land_count` and `land_drops`: P(at least N lands seen by turn N).
- `mulligan_looks`: `L`, the most seven-card hands the mulligan can look at, from the format's free mulligans. Read it from the report; never assume a number.
- `assumptions`: the tier-1 assumptions block. Its first line states the format's draw rule.

Echo every line of the `assumptions` block next to the numbers. Do not shorten or skip a line. The report carries no floor, no confidence interval, and no seed. It carries no citation objects. Name cards as the report's `input` echo names them, and add no footnote for them. Its `versions` block holds only the `format`, so the versions line of an answer built from tier 1 alone names only the format.

When the question is about mana sources, name each excluded card and its `reason` next to the `by_color` counts. Without these names, the user sees a source in the deck that the counts leave out.

**Consistency target.** The report returns raw probabilities only. If the user asks "is this enough", state a threshold as your own declared judgment. Example: "I treat 85% by turn 3 as enough for a ramp piece; the service sets no target." Never present the threshold as a service result.

## Bracket: `POST /bracket-classification`

The request is the decklist, a numeric `target_bracket`, and optional `assumptions`.

Resolve the user's power-level phrase with `../../references/phrase-to-bracket.md`. Send the number. The service never sees a phrase. If the phrase matches no row, ask the user which bracket they mean before you call. If the user names no target at all ("what bracket is this deck"), ask which bracket they mean to play at. The service needs the number. The floor comes back the same whatever target you send.

```json
{
  "decklist": { "...": "..." },
  "target_bracket": 3,
  "assumptions": { "intent_bracket": 3, "chains_extra_turns": false }
}
```

`assumptions` holds what no decklist proves: `intent_bracket` (the bracket the user means to play at) and `chains_extra_turns`. Send only what the user told you. An absent assumption comes back as unknown.

Report all four parts. Never report a single bracket value alone.

- `bracket_floor`: the minimum bracket the decklist proves, the target, whether it is within the target, and the basis.
- `assumed_bracket`: the floor raised by the declared assumptions, or unknown when the user declared nothing.
- `reasons`: one entry per barometer that fired, with its count, the threshold text, and the cards. Cite each card.
- `assumptions`: each input, whether the user declared it, and its effect.

Declare the mapping you used next to the bracket: "Treating 'upgraded' as bracket 3." The response cites the bracket spec and the Game Changers snapshot; render both as footnotes.

## Tier 2: `POST /simulate`

Translate the user's goal into the closed goal-predicate vocabulary. There are three kinds:

| The user says | Goal |
|---|---|
| "X on the battlefield by turn N", "have X out by turn N" | `{ "kind": "on_battlefield", "card": "X", "by_turn": N }` |
| "cast X by turn N", "resolve X by turn N" | `{ "kind": "cast", "card": "X", "by_turn": N }` |
| Two or more of the above together | `{ "kind": "all_of", "goals": [ ... ] }` |

`on_battlefield` is point-in-time: the card is on the battlefield at the end of turn `by_turn`. `cast` counts resolved casts only. `all_of` needs every member met. `card` is the exact name of a card in the decklist. A goal outside this vocabulary cannot run. Say so, and offer the nearest predicate.

On a multi-face card (modal double-faced, transform, split, adventure), `card` takes one of three names. The full card name, as the decklist entry carries it, matches either face: `Esika, God of the Tree // The Prismatic Bridge`. The front face name matches the front face only: `Esika, God of the Tree`. The back face name of a modal double-faced card matches that face only: `The Prismatic Bridge`. The engine casts that face, or plays it as a land.

A goal on any other face cannot run: a transform back face, the second half of a split or adventure card, a meld or flip face. Say so. Offer the front-face goal as the nearest predicate. State the substitution next to the probability.

An MDFC land has a spell front face and a land back face: `Shatterskull Smashing // Shatterskull, the Hammer Pass`. The engine plays it as a land when no other land is in hand. It never does so while the goal wants the front face. A pathway enters as the face that makes a color the goal needs, and as its front face on a tie. The mulligan and tier 1 count an MDFC land as a land. Tier 1 counts a pathway as a source of both colors.

```json
{
  "decklist": { "...": "..." },
  "goal": {
    "kind": "all_of",
    "goals": [
      { "kind": "on_battlefield", "card": "Sol Ring", "by_turn": 1 },
      { "kind": "cast", "card": "Atraxa, Praetors' Voice", "by_turn": 4 }
    ]
  },
  "runs": 1000,
  "turn_horizon": 10
}
```

Leave `runs` at the default of 1000 unless the user asks for more. Set `turn_horizon` to at least the goal's last turn. Omit `seed` unless the user wants to replay a run.

Report `probability.label` (the floor, ">= X%, greedy policy"), `probability.confidence_interval` (the 95% interval), `runs`, and `base_seed`, all four together. Never report `probability.floor` as a point estimate. Add the per-turn `curve` when the user asks how the odds grow. Add an exemplar line when the user asks what a success looks like. Name the engine and its version from `engine.name` and `engine.version` next to the floor. Report `engine_coverage`: a card in `unimplemented` is a blank card that never casts, and a card in `unprofiled` runs without pilot features. Both lower the floor. Report `engine_errors` when it is above 0; each such game counts as a failure. The envelope's `deck_verdict` is the same verdict as `POST /deck-verdict` for the list and the format, so make no second call. Report it as the Format section says when its `status` is not `legal`. An illegal deck still runs, and its floor stands. Echo the `simplifications` list next to the floor. The engine plays no combat and no opponent that acts, and the user must see that before they read the number. Interactive disruption is the one exception: in its disrupted arm a scripted opponent casts the profile's disruption cards (see Tier 4).

## Tier 3: resilience

A resilience question is a tier-2 call with a `disruption_profile`. Build the goal as above. If the user names the disruption, send it as `[{ kind, turn }]` from the closed vocabulary. If the user names none, send the shared default from `../../references/default-disruption-profile.md`. Add `board_wipe` only under that file's two conditions. `runs` counts pairs here, so the request plays twice as many games. Keep the default.

```json
{
  "decklist": { "...": "..." },
  "goal": { "kind": "on_battlefield", "card": "Sol Ring", "by_turn": 3 },
  "disruption_profile": [
    { "kind": "counterspell", "turn": 3 },
    { "kind": "targeted_removal", "turn": 4 }
  ]
}
```

Report from the `disruption` block: the baseline floor, the disrupted floor, and the `resilience_delta` between them. Report each entry of `events`: its `fired_rate`, and whether it is `unused`. Echo the profile as an assumption next to the delta. Echo the tier-3 `assumptions` block: no protection, blind pilot, adversarial targeting, no indestructible.

Tier 3 counts none of the deck's own answers. After a tier-3 report, offer tier 4 in one line: "Tier 4 can show whether your counterspells and protection answer these events; want that run?"

## Tier 4: interactive disruption

Tier 4 is a tier-3 request with `disruption_mode` set to `"interactive"`. The disrupted arm then plays against a scripted opponent. It casts each event of the profile as a disruption card: Counterspell for `counterspell`, Atomize for `targeted_removal`, Rout for `board_wipe`. The deck gets one response window per card. The baseline arm is the same tier-2 run in both modes. A hold run has its own baseline.

To show what the deck's answers add, post the same request and the same `seed` two times: once with `disruption_mode` set to `"abstracted"`, once with `"interactive"`. Pick a seed for the first call, or send the first call's `base_seed` in the second. The two baseline arms are then equal. Build the goal and the profile as in tier 3. With no profile from the user, send the default from `../../references/default-disruption-profile.md`.

```json
{
  "decklist": { "...": "..." },
  "goal": { "kind": "on_battlefield", "card": "Pithing Needle", "by_turn": 4 },
  "disruption_profile": [
    { "kind": "counterspell", "turn": 3 },
    { "kind": "targeted_removal", "turn": 4 }
  ],
  "disruption_mode": "interactive",
  "runs": 1000,
  "seed": 42
}
```

`runs` counts pairs, as in tier 3. Keep the default of 1000. Send at most 10000 pairs, the interactive cap, when the user asks for more.

### The hold run

Tier 4 has a third run when the deck has response cards. Read `disruption.response_cards` from the interactive envelope.

- If the list is empty, make no third call. With no response card, the hold policy acts like the default policy. Say in one line that the deck has no response cards, so holding up answers changes nothing.
- If the list is not empty, post the request a third time. Send the same `seed`, the same profile, `"disruption_mode": "interactive"`, and `"play_policy": "goal-aware-hold"`.

Send `play_policy` only in the hold run. The service returns 400 `invalid-play-policy` when a request sets `goal-aware-hold` without interactive mode. The hold run is part of tier 4. Do not offer it as a separate tier.

### Tier 4 report

Report these from the envelopes, side by side. The hold run, when made, adds a third envelope:

- Each envelope's `disruption_mode` echo, so the user sees which mode gave which number.
- Each envelope's `policies.play` echo, next to that run's floors, so the user sees which policy gave which number.
- The baseline floor once for the abstracted and interactive runs, and both of their disrupted floors, each with its `probability.label` and 95% confidence interval.
- The `resilience_delta` of each envelope.
- Per event, from the interactive envelope's `events`: `fired_rate`, `answered_rate` and `not_cast_rate`. Here "fired" means the opponent cast the card. Give the abstracted `fired_rate` next to them. Each rate is a share of all runs, not of the runs where the card was cast. Say "of all runs" next to `answered_rate`. For the share of cast cards, divide `answered_rate` by `fired_rate` and say that you did.
- The response cards: `disruption.response_cards`. Add an exemplar's `answered_by` when the user asks what a save looks like.
- The unprofiled responses: `engine_coverage.unprofiled_responses`. These cards never answer, so the answered rate is a lower bound. Say so.

The difference between the abstracted and interactive disrupted floors mixes the real-card rules with the deck's answers. Say that in one line next to the floors. Do not call the difference the value of the deck's answers alone.

Echo the interactive `assumptions` block next to the interactive floor: the disruption cards, the opponent's mana rule, and every other line. Quote each line word for word, as the envelope gives it. Do not shorten, merge or split a line, and do not drop a clause. The disruption-cards line keeps each card's cost, colors and type. Echo each envelope's `simplifications`: under interactive, the "Scripted opponent" line replaces the "Idle opponent" line.

Report the hold run as its own result, labeled "if the pilot holds up answers". Give its baseline floor, its disrupted floor, and its `resilience_delta`. Give each floor with its label, 95% interval, runs, base seed, and engine. Keep the label ">= X%, greedy policy" on all three runs. Echo every line of the hold envelope's interactive `assumptions` block next to the hold floors, word for word. Quote a line once for both runs only when the two blocks give the same words. The block adds the policy, the reserve, and the limits. The block drops the tap-out sentence.

Tell the user that the hold baseline differs from the other baseline. Both hold arms play the hold policy (ADR 0014 measured 80.1% against 77.5%). Compare each disrupted floor only with the baseline of its own run. Never merge the three results into one number.

## Format

- `POST /deck-verdict` judges card legality and deck shape for one `decklist` in one `format`. It runs no simulation. The format defaults to `commander`.
- Make one call per format the user names. "Which Arena Brawl formats can this deck go to" is three calls: `brawl`, `competitive_brawl`, and `standard_brawl`.
- Put the companion under `companion`, not under `entries`. It does not count toward the deck size.
- Report the `status`: `legal`, `incomplete`, `illegal`, or `unknown`. For `incomplete`, give the number of missing cards: `required_deck_size` minus `deck_size`.
- Report each entry of `card_findings` and `shape_findings` with its citation. A Brawl card finding cites its `citation`, of type `legality`. A Commander ban cites its `ban_event`. A shape finding cites its comprehensive rule.
- Write one answer section per format, each with its own verdict and citations. End with one merged versions block.
- A verdict lists blockers only. It says nothing about cards that pass. For each Brawl card finding with the status `banned`, call `GET /cards?name=` and report that card's status in the other Brawl formats, with its `legality` citation.
- For each Brawl card finding with the status `not_legal`, call `GET /cards?name=` to read `digital_only` only. The verdict already cites the status.
- Read `digital_only` in each card detail you fetch for a `banned` or `not_legal` finding.
- When the card detail has `digital_only` true, name the card a digital-only card in that format's section. An example is "Perennial Gravewarden is a digital-only card". Cite it as a `card` footnote with the card detail's `oracle_id`. The flag does not name the digital set. Do not say Alchemy unless a cited source says so.
- A Brawl format takes no `banlist_version`; a pin with one is a `400`.
- A bracket question about a Brawl deck has no answer. Make no call, and reply: "No power target: Arena publishes no bracket system for Brawl."

```json
{
  "decklist": {
    "commanders": [{ "name": "Old Stickfingers" }],
    "companion": { "name": "Umori, the Collector" },
    "entries": [{ "card": { "name": "Swamp" }, "quantity": 30 }]
  },
  "format": "competitive_brawl"
}
```

## Report

Render every answer per `../../references/answer-contract.md`:

- Citations as numbered footnotes with their fetch path. Card citations come from the bracket reasons and the simulate envelope. Tier 1 returns none. A simulate run has no citation: give its seed and run count next to its floor, not in a footnote.
- The `versions` block once, at the end, merged across every response the answer used.
- Assumptions next to the numbers they shape. These are the tier-1 assumptions block, the floor label with its 95% confidence interval, the phrase-to-bracket mapping, the disruption profile and mode, and the category membership.

Lead with the answer to the question asked. Keep the tables the question needs. Leave out the rest.
