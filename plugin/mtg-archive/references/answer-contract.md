# Answer contract

Every skill in this plugin reports the same way. This file is the one place that says how. Read it before you answer.

## Reach the service

The service is plain HTTP and JSON. There is no MCP layer. Call it with curl.

The default address is `http://127.0.0.1:8080`. The env var `MTG_ARCHIVE_URL` overrides it. No other setting changes the address.

```sh
: "${MTG_ARCHIVE_URL:=http://127.0.0.1:8080}"
curl -sS "$MTG_ARCHIVE_URL/healthz"
```

Every request body and every response uses snake_case. Errors are `application/problem+json`. Read the `detail` and the extension members. Report them.

## Down service

If the service does not answer, tell the user to start the compose stack from the repo root:

```sh
docker compose up
```

Then stop. Never answer from model recall while the service is down.

## Citations

Every fact you report carries a citation the service returned. A citation is one object:

```json
{ "type": "card|rule|ban_event|legality|snapshot", "id": "...", "label": "..." }
```

Render citations as footnotes. Number them in order of first use. Each footnote shows the label and the fetch path for its id:

| `type` | Fetch |
|---|---|
| `card` | `GET /cards/{id}` |
| `rule` | `GET /rules/{id}` |
| `ban_event` | `GET /ban-events/{id}` |
| `legality` | `GET /cards/{id}` |
| `snapshot` | `GET /{id}` (the id is a path such as `game-changers/2026-02-09` or `bracket-spec/2025-10-21`) |

A `legality` citation is a Brawl format's legality status for one card. Its id is the oracle id. Its label states what the key says: `<key> <key status> as of <date>`, for example "competitivebrawl banned as of 2026-10-03". The key never holds `not_eligible`, so a `not_eligible` row adds the fact the service derives, for example "brawl legal as of 2026-10-03; not eligible as commander". The card detail row for that format holds the Scryfall key, the Scryfall URL, and the as-of date. A Commander ban cites its `ban_event`, never `legality`.

Do not add a citation the service did not return. Do not cite prose. If the archive lacks a fact, say so.

A simulate run has no citation. Give its seed, run count and engine next to its floor. Add no footnote for a run.

## Versions block

Every computed response carries a top-level `versions` block. Echo it once per answer, at the end, with each axis the response filled. The `format` axis comes first. A Brawl format fills `legality_as_of` in place of the banlist version:

```text
Versions: format commander, banlist 2026-04-01, bracket spec 2025-10-21, Game Changers 2026-02-09, CR 2026-03-15.
Versions: format competitive_brawl, Brawl legality as of 2026-10-03 (Scryfall), popularity edhrec as of 2026-10-03, CR 2026-03-15.
```

A Commander answer shows "format commander" too, so the default is visible. Card detail covers all four formats and fills no `format` axis: echo the format of the rows you report.

Leave out an axis the response did not fill. When one answer composes several responses, merge their blocks. If two responses disagree on one axis, report both values. An answer that covers several formats has one section per format, each with its own verdict and citations, and ends with one merged versions block.

## Assumptions

State each assumption next to the number it shapes. Each of these is an assumption:

- **Phrase-to-format mapping.** State the format you read from the user's wording, and the words you read it from. See `phrase-to-format.md`. Put it at the top of the answer, because the format shapes every number after it.
- **Floor label.** A tier-2, tier-3 or tier-4 probability is a floor ("≥ X%") with its 95% confidence interval, run count, and base seed. Report all four together.
- **Engine simplifications.** Echo the `simplifications` list a simulate envelope carries next to its floor. It states what the engine does not model: no combat, no opponent that acts, sorcery-speed casts. Under interactive disruption, a "Scripted opponent" line replaces the "Idle opponent" line.
- **Engine.** Name the engine and its version (`engine.name`, `engine.version`) next to each tier-2, tier-3 or tier-4 floor. Floors from different engines or versions do not compare. Report `engine_coverage`: an unimplemented card plays as a blank card, and an unprofiled card runs without pilot features. Report `engine_errors` when it is above 0; each such game counts as a failure.
- **Tier-1 assumptions.** Echo every line of the `assumptions` block the tier-1 report carries. Do not shorten or skip a line.
- **Phrase-to-bracket mapping.** When a power-level phrase resolves to a bracket, state the phrase and the bracket. See `phrase-to-bracket.md`.
- **Disruption profile.** When a resilience answer uses the default profile, state the profile. See `default-disruption-profile.md`. State the `disruption_mode` each envelope echoes next to its disrupted floor.
- **Play policy.** Echo `policies.play` next to each tier-4 floor. A hold run and a greedy run have different baselines. Compare a disrupted floor only with the baseline of the same run. Never compare a hold disrupted floor with a greedy baseline.
- **Scripted opponent.** Under interactive disruption (tier 4), echo the disruption cards, the opponent's mana rule, and every line of the interactive `assumptions` block next to the interactive floor. Quote each line word for word. Do not shorten, merge, split or skip a line, and do not drop a clause. Do the same for the hold run's interactive `assumptions` block next to the hold floors; it differs from the greedy block. Quote a line once for both runs only when the two blocks give the same words.
- **Popularity order.** When the popularity order sorts or breaks ties in an answer, name its source and as-of date from `versions.popularity_source` and `versions.popularity_as_of`. The order is per format, and `popularity_rank` is a card's place in it. When the source is `edhrec` in a Brawl format, the order is a proxy. Say so: "Ordered by Commander popularity (EDHREC rank) as a proxy; no Brawl popularity data is used." Never present EDHREC rank as Brawl play data.
- **Name rewrites.** When a deck response carries `name_rewrites`, state each one: the name as sent and the card it resolved to. An Arena `A-` (rebalanced) name that does not resolve resolves to the original card.

A skill may add assumptions of its own. Each one follows the same rule: state it next to the number it shapes.
