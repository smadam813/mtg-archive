---
name: mtg-lookup
description: "Answer any MTG card, rules, interaction, or legality question in Commander or an Arena Brawl queue from the MTG Archive service. Use for every MTG fact question, in any conversation, even a passing one. Cite the archive or say it lacks the fact."
---

Read `../../references/answer-contract.md` first. The path is relative to this file. It states how to reach the service, the down-service rule, the citation footnotes, and the versions block. This file adds only what is specific to lookup.

Read `../../references/phrase-to-format.md` to turn the user's wording into a format. The Format section below lists what changes in this skill.

## Trigger

Use this skill for any MTG fact question, in any conversation. That includes:

- What a card does, what it costs, and its type line.
- What a rule or glossary term says.
- How two or more cards interact.
- Whether a card is legal in Commander or an Arena Brawl format in a given role, and the date of any ban or unban.
- Which cards do a thing ("cheap green ramp", "wraths that hit indestructible").

Do not analyze a decklist here. Mana, bracket, consistency, and resilience questions belong to `deck-check`, and so do tier 4 questions on whether a deck's own counterspells or protection help. Hand them over.

## Source of truth

Every fact comes from the service. Never answer from model recall, even for a card you know well. If the service does not answer, follow the down-service rule in the answer contract and stop. If the archive lacks the fact, say so and stop.

Set `MTG_ARCHIVE_URL` once per session with the line the answer contract gives. Every route below is relative to it. Bodies and responses are snake_case JSON.

## Routes

The skill composes seven routes. Three search and four resolve.

| Route | Use |
|---|---|
| `POST /cards/search` | Structured filters over legal cards. No embedding. |
| `POST /cards/semantic-search` | Card search route: "what should the card do" in natural language plus the same filters. |
| `POST /rules/search` | Rules search route: a rules question in natural language over CR rule chunks and glossary chunks. |
| `GET /cards/{oracle_id}` | Card detail. The target of every `card` citation. The only route that serves rulings. |
| `GET /cards?name=` | Exact-name lookup. Not search. |
| `GET /rules/{number}` | One CR rule chunk or glossary chunk, verbatim. The target of every `rule` citation. |
| `GET /ban-events/{id}` | One dated ban event. The target of every `ban_event` citation. |

## Resolve a named card

When the user names a card, fetch it by exact name:

```sh
curl -sS -G "$MTG_ARCHIVE_URL/cards" --data-urlencode "name=Rhystic Study"
```

`GET /cards?name=` is exact-name lookup only. It is case-sensitive and matches the full Oracle name. Use it for a name the user typed, or to check a name you saw in a hit. To follow a `card` citation, use `GET /cards/{oracle_id}` with the citation's `id`. Never use it as fuzzy search.

A misspelled or partial name is a `404` with `unknown-card-name`. On that problem, do not guess a spelling. Run `POST /cards/search` with the `name` filter, a case-insensitive substring. Show the user the candidates, or ask.

A `500` with `ambiguous-card-name` lists `oracle_ids`. Fetch each by `GET /cards/{oracle_id}` and report the corpus problem.

Card detail carries name, mana cost, type line, Oracle text, faces, colors, color identity, and keywords. It also carries per-role `legality`, `rulings`, Oracle Tags, and `game_changer`. Read the whole record before you answer.

## Rulings

Rulings live on card detail and nowhere else. `GET /cards/{oracle_id}` serves them as `rulings`, each with `source`, `published_at`, and `comment`. Search hits carry no rulings. When an interaction question needs a ruling, resolve each card to its detail first. Then read the rulings there.

## Legality

Card detail's `legality` lists one row per format and role. Each row names its `format` and its `role`: `commander`, `companion`, or `main`. Each row is `legal`, `banned`, `not_legal`, or `not_eligible`. Read the rows of the format the user asked about. Cite each row as the Format section says.

`legality` is absent until a legality rebuild has run. If it is absent, or it has no row for the format, say the archive lacks legality for this card. Do not infer legality from the card's presence.

## Format

- Card detail shows all five formats in one call: `commander`, `brawl`, `competitive_brawl`, `standard_brawl`, and `standard`. Do not call it once per format.
- Card search takes `format` in the body, with `commander` as the default. Send the format you read from the user's words. A Brawl format takes no `banlist_version`; a pin with one is a `400`.
- A Commander `banned` row carries a `ban_event` citation. Fetch it:

  ```sh
  curl -sS "$MTG_ARCHIVE_URL/ban-events/$ID"
  ```

  The event carries the card, the action, the scope, and the banlist version with its announcement URL. A card banned by a category line (ante cards, Conspiracy cards, the 2020 removals) also carries `category`. Cite the event, not your memory of the announcement.
- To ask about a past Commander banlist, pass `banlist_version=yyyy-MM-dd` on detail or in a search body. On detail, the pin changes the Commander rows only. An unknown version is a `400`, not a fallback. Report it.
- A Brawl legality fact cites `legality`, not a ban event. A Brawl row carries no `ban_event`. Its `citation` is the `legality` citation, and the row holds `scryfall_key`, `scryfall_uri`, and `legality_as_of`. Cite every Brawl status this way, `legal` and `not_legal` too.
- A Brawl format has no banlist history. For a past Brawl state, say the archive holds the current state only, as of `legality_as_of`.
- A `standard` row cites `legality` like a Brawl row. It carries no `ban_event`, takes no `banlist_version`, and dates from `legality_as_of`. A `standard` search with a `banlist_version` is a `400`.
- Standard is not Standard Brawl. Read the `standard` rows for Standard and the `standard_brawl` rows for Standard Brawl.
- Footnotes: `ban_event` comes from a Commander legality row. `legality` comes from a Brawl legality row.
- Versions block: card detail fills `banlist_version` for the Commander rows. For Brawl rows, take the as-of date from the row's `legality_as_of`.

## Rules

For a rule the user cites by number, fetch it:

```sh
curl -sS "$MTG_ARCHIVE_URL/rules/601.2b"
```

A subrule number resolves to the rule chunk that contains it. A bare section number ("601") is unknown. A glossary term is an exact term ("Hexproof"). Quote the text verbatim.

For a rules question in prose, search:

```sh
curl -sS -X POST "$MTG_ARCHIVE_URL/rules/search" \
  -H 'content-type: application/json' \
  -d '{"query": "does a copy of a permanent spell enter the battlefield", "k": 20}'
```

Hits are `rule` or `glossary` chunks, nearest first. Each hit carries the chunk text verbatim, a `citation`, and a `distance`. Cite the hit. You do not need to fetch the chunk again. There is no legality axis.

## Card search

For "which cards" questions, use the card search route:

```sh
curl -sS -X POST "$MTG_ARCHIVE_URL/cards/semantic-search" \
  -H 'content-type: application/json' \
  -d '{"query": "destroy all creatures, even indestructible ones", "k": 30, "color_identity": ["W","B"]}'
```

Filters compose with the query: `types`, `keywords`, `tags`, `min_mana_value`, `max_mana_value`, `enters_tapped`, `name` (substring), `role`, `color_identity`, `collection_scope`, `format`, and `banlist_version`. A card must match every value in an array filter. A `tags` value can be a Scryfall tag alias; the service resolves it to the label ("blink" matches cards tagged "flicker"). `role` defaults to `main`. Legality is a hard filter at the boundary. A hit is always legal in the queried role.

### Archetype slang

Commander slang ("stax", "voltron", "aristocrats", "board wipe") has no token in card text, so the card search route lands on name matches. Before you search from the user's wording, read `../../references/archetype-glossary.md`. Apply it as that file says. In short: pull by each mapped tag and type with `POST /cards/search`. Then send the semantic query with the glossary phrasing in place of the term. Merge with the pulled cards first. State the expansion as an assumption: the term, the tags and types pulled, and the phrasing sent.

For a pure structured filter, use `POST /cards/search` with the same filters and no query. An example is "all legendary Elves with mana value 3 or less". It pages in the format's popularity order with `limit` and `offset`.

Both responses carry `candidates`. Each hit carries a `card` citation, mana cost, type line, Oracle text, color identity, keywords, and `popularity_rank`, its place in the popularity order. Semantic hits add `face_index` and `distance`. `k` counts cards. A card's distance is the distance of its nearest face, so a card with two faces takes one slot.

## Semantic disabled

Both search routes that embed, `POST /cards/semantic-search` and `POST /rules/search`, can answer with `semantic.enabled` false and no hits. `semantic.disabled_reason` says why. Report the reason to the user. For a card question with a structured form, fall back to `POST /cards/search`. For a rules question, fetch by number if the user gave one. Do not answer from recall.

## Re-rank the hits

The service returns filtered KNN hits, nearest first. It has no re-ranker and no fusion. Distance is a similarity signal, not a verdict. You re-rank:

1. Ask for more hits than you will show. Use `k` of 20 to 50.
2. Read each hit's Oracle text or rule text against the question.
3. Drop hits that match on vocabulary but not on function.
4. Order the rest by how directly they answer the question. Two hits with `distance` values within 0.003 are a tie: a distance reproduces only to about 0.003 across runs. Break ties by `popularity_rank` for cards. State the popularity source as `answer-contract.md` says.
5. Show the top few. Say how many hits came back and how many you dropped.

When a question spans cards and rules, run both search routes. Merge the hits by judgment. Say which route each fact came from.

## Interactions

For "what happens when X and Y" questions:

1. Resolve each card by name to its detail.
2. Read Oracle text and rulings.
3. Search the rules for the mechanic in question.
4. Answer step by step. Cite the card, the ruling's card, or the rule chunk beside each step.

If no ruling or rule chunk in the archive settles the question, say so. Do not fill the gap.

## Report

Follow the answer contract. Each fact carries a footnote with a citation the service returned. Each footnote shows the label and the fetch path for its id. `card` comes from detail or a hit. `rule` comes from a rule chunk or a hit. The Format section gives the legality footnotes. Number footnotes in order of first use.

Echo the merged `versions` block once at the end. Card search fills `format`, and `banlist_version` or `legality_as_of`. For card detail, echo the format of the rows you report, and fill the legality axis as the Format section says. Rules search and rule chunks fill `cr_effective_date`.

State the assumptions that change the answer:

- The phrase-to-format mapping, at the top of the answer, for every legality fact or search.
- The role you queried, when the user did not name one.
- The banlist version, when the user asked about the past.
- The color identity filter, when the user named a commander and you took the identity from its detail.
- The glossary expansion, when the user's wording contained an archetype term: the term, the tags and types pulled, and the phrasing sent.
