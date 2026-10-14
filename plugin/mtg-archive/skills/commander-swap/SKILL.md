---
name: commander-swap
description: "Find alternative commanders that keep most of an existing deck after a ban, a commander change, or a move to another Brawl queue where the commander is banned. Use when the user asks what to replace a commander with."
---

Read `../../references/answer-contract.md` first. It states how to reach the service, how to cite, and how to report.

Read `../../references/phrase-to-format.md` to turn the user's wording into a format. The Format section below lists what changes in this skill.

## Trigger

The user has a decklist and wants a new commander for it. The old commander is banned, or the user wants to change it. The question is "which commander keeps most of this deck?".

This skill does not build or tune the 99 for the new commander. That is `build-deck`.

## Constants

| Constant | Value | Meaning |
|---|---|---|
| Pull depth | 200 | The number of candidates the service returns, in retained order. It is the service maximum. |
| Presentation depth | 5 | The number of candidates the answer shows, in re-ranked order. |
| Semantic depth | 200 | The number of candidates that get a semantic distance. It equals the pull depth. |
| Reading depth | 20 | The number of candidates you rank by reading when semantic search is disabled. |

All four numbers are skill constants. The user cannot change them by phrasing. State the pull depth and the presentation depth in the answer.

## Inputs

You need the full decklist: the commander or commanders, the companion if the list has one, and every card of the main deck with its quantity. If the user gives no list, ask for it. Do not guess cards.

Send pasted deck text (an Arena export or a `<qty> <name>` list) to `POST /decklist-parse` as `text`, show its `notes`, and use its `decklist`.

In Commander, when the list marks no commander, fetch the latest version from `GET /banlist/versions`. Then fetch its `banned[]` with `GET /banlist/{version}`. A card with scope `all` cannot sit in the 99. When exactly one card in the list has scope `all`, treat it as the commander. State the assumption with its ban footnote. Otherwise ask the user which card is the commander. A `commander_only` ban leaves the card legal in the 99, so it does not pick the commander.

Collection scope is the caller's choice. Set it explicitly on every request; the service never infers it. The default is `false`. Set it to `true` only when the user asks to limit candidates to cards they own. The answer states which value you used.

## Steps

### 1. Check the service

Run the health check from the answer contract. If the service is down, tell the user to run `docker compose up`. Then stop.

### 2. Pull the candidates

Call `POST /commander-alternatives` with the decklist, the format, the pull depth, and the scope. The response can exceed 2 MB. Do not print it. Save it to a file:

```sh
mkdir -p /tmp/commander-swap
curl -sS -X POST "$MTG_ARCHIVE_URL/commander-alternatives" \
  -H 'content-type: application/json' \
  -o /tmp/commander-swap/alternatives.json \
  -d '{
    "decklist": {
      "commanders": [{ "name": "Old Commander" }],
      "companion": { "name": "Jegantha, the Wellspring" },
      "entries": [
        { "card": { "name": "Sol Ring" }, "quantity": 1 },
        { "card": { "name": "Forest" }, "quantity": 10 }
      ]
    },
    "format": "commander",
    "collection_scope": false,
    "limit": 200
  }'
```

The request has no color identity field, and the skill adds no color identity filter. Keeping color identity is the retention metric itself.

The service ranks by `retained_count` descending, then by `color_identity_distance` to the old commander, then by `popularity_rank`, the place in the commander popularity order. This order is set arithmetic. State the popularity source as `answer-contract.md` says. Keep it as the tie-break for step 4.

The response carries:

- `original`: the old commander citations, the combined color identity, and `deck_size`.
- `candidates[]`: each with `card`, `color_identity`, `popularity_rank`, `available` (scope only), `retained_count`, `lost_count`, `color_identity_distance`, `retained[]`, `lost[]`, and `companion` (only when the list has one).
- `versions`.

Each entry of `retained[]` carries `card` and `quantity`. Each entry of `lost[]` carries `card`, `quantity`, `reason`, and `citation`. The reason is `banned`, `not_legal`, or `color_identity`. A `not_legal` card is outside the format's card pool. In Commander, a `banned` loss cites the standing ban event. When the archive holds no event for the ban, the citation is the card itself. A Commander `not_legal` loss cites the card. A `color_identity` loss cites the card.

Parse the response with `jq`. Keep each candidate's id, name, `retained_count`, `color_identity_distance`, and `lost_count`, in service order:

```sh
jq -c '{original, versions, detail, unresolved_names, candidates: [.candidates[]? | {id: .card.id, name: .card.label, retained_count, color_identity_distance, lost_count}]}' \
  /tmp/commander-swap/alternatives.json
```

Never extract `retained[]`. Extract `lost[]` only for the 5 candidates you present, in step 5.

A problem response carries `detail` and no `candidates`. Report `detail`. An unresolved decklist lists the failed names in `unresolved_names`. Ask the user to fix them. A decklist without a commander is a request error. Ask for the commander.

### 3. Fetch the old commander's function

Fetch each old commander with `GET /cards/{oracle_id}`, using the ids in `original.commanders[]`. Read the legality row with `format` equal to the request format and `role` equal to `commander`. In Commander, when the `status` is `banned`, keep its `ban_event` citation for step 5. Take the Oracle text. For a card with faces, join the `oracle_text` of every face. With two commanders, join their text.

Write that text to `/tmp/commander-swap/query.txt`. Then call `POST /cards/semantic-search`:

- `query`: the text.
- `role`: `commander`.
- `collection_scope`: the same value as step 2.
- `oracle_ids`: the pulled ids.
- `k`: the semantic depth.

Build the body with `jq`, so that `jq` escapes the text and the ids:

```sh
jq -c --rawfile query /tmp/commander-swap/query.txt \
  '{query: $query, role: "commander", collection_scope: false, k: 200, oracle_ids: [.candidates[].card.id]}' \
  /tmp/commander-swap/alternatives.json > /tmp/commander-swap/semantic-request.json
curl -sS -X POST "$MTG_ARCHIVE_URL/cards/semantic-search" \
  -H 'content-type: application/json' \
  -d @/tmp/commander-swap/semantic-request.json \
  | jq -c '{semantic, versions, candidates: [.candidates[]? | {id: .card.id, distance}]}'
```

`oracle_ids` limits the search to the pulled candidates. `k` counts cards, so every pulled candidate with a card document gets a distance.

The query is the old commander's Oracle text, never the user's wording, so the archetype glossary (`../../references/archetype-glossary.md`) does not apply to this call. If the user also names an archetype for the new commander ("a voltron commander"), that is a judgment input for step 4, not a query change.

Read `semantic.enabled` first. When it is `false`, the response has no `candidates`. Report `semantic.disabled_reason` as an assumption. Then rank by the fallback in step 4.

Each entry of `candidates[]` carries `card.id` and `distance` (cosine distance; smaller is closer). Join them to the pulled candidates on `card.id`.

### 4. Re-rank by function

The re-rank is agent-side, never service-side. Do not ask the service to score, fuse, or re-rank. It returns set arithmetic only.

A **retention tier** is the set of candidates that share one `retained_count` and one `color_identity_distance`. The service order keeps each tier together. Never re-rank across a retention tier: a candidate that keeps more of the deck always ranks higher.

Order the pulled candidates by these keys, in this order:

1. `retained_count`, larger first.
2. `color_identity_distance`, smaller first.
3. The semantic `distance`, smaller first. Two distances within 0.003 are a tie: a distance reproduces only to about 0.003 across runs. A candidate without a distance ranks below the candidates in its tier that have one.
4. The service order from step 2.

Then take the top 5.

When semantic search is disabled, take the first 20 candidates in service order (the reading depth). Fetch each one's text with `GET /cards/{oracle_id}`. Within each tier, order them by your reading of the text against the old commander's text. Keep the tiers. Then take the top 5.

State the similarity measure as an assumption. It is the semantic distance from each candidate's card document to the old commander's Oracle text. When semantic search is disabled, it is your own reading.

The top tier fills the pull when the service returns 200 candidates and the last one is in the first one's tier. Then state this assumption: the tier can hold more commanders than the pull depth. The answer compares only the 200 most popular of them.

### 5. Present

Open with each old commander's name and its card footnote. When a commander is banned, show its ban footnote next to its name.

Extract the full entry of each of the 5 candidates with `jq`, one id at a time:

```sh
jq -c --arg id "<candidate card id>" \
  '.candidates[] | select(.card.id == $id) | {card, color_identity, popularity_rank, available, retained_count, lost_count, lost, companion}' \
  /tmp/commander-swap/alternatives.json
```

Fetch each one's Oracle text with `GET /cards/{oracle_id}`, unless you fetched it for the fallback.

For each of the 5 candidates, in re-ranked order, show:

- The card name with its citation footnote, its color identity, and its `popularity_rank`.
- Under scope: `available`.
- Why it is a functional match, in one or two sentences, quoting the fetched Oracle text.
- Which measure ranked it within its tier: the semantic distance, your reading of the text, or the service order when it has no distance. Name the measure for every candidate.
- `retained_count` of `deck_size`, and `lost_count`.
- Every lost card with its quantity and its reason. A `banned` loss shows the ban footnote. A `color_identity` loss says the card's identity escapes the candidate's, with the card footnote. List every loss, grouped by reason. No uncited losses, and no losses left out for length.

Show a candidate with no losses as "keeps the whole deck".

When the list has a companion, show the candidate's `companion` entry: kept, or lost with its reason and citation footnote.

State once, near the counts, whether you applied collection scope.

## Format

- Send `format` on every `POST /commander-alternatives` call. The default is `commander`. A move to another format ("move this deck to Competitive Brawl") names the destination as the request format.
- The pool is the format's commander-legal cards. In a Brawl format that includes legendary planeswalkers, and legendary Vehicles or Spacecraft with power and toughness.
- In a Brawl format, when the list marks no commander, ask the user which card is the commander. The ban-based inference is Commander-only.
- A Brawl `banned` or `not_legal` loss cites its `citation`, of type `legality`. Render it with `GET /cards/{id}`. A Commander `banned` loss cites its `ban_event`, and a Commander `not_legal` loss cites the card.
- `popularity_rank` is the place in the request format's popularity order. It breaks ties as in step 4. State its source as `answer-contract.md` says.
- Put the companion under `companion`, not under `entries`. Each candidate then carries a `companion` entry. The same rules judge it as the deck. It is not in `retained_count` or `lost_count`.
- The query does not judge deck size. When `deck_size` differs from the format's main-deck size (99, or 59 in Standard Brawl), state the gap and point to `deck-check`. Give no cut list.
- Show the old commander's status in the request format from `GET /cards/{oracle_id}`: the legality row for that format and the `commander` role. For a Brawl format, show its `citation` (type `legality`) next to the name, not a `ban_event`.
- A Brawl format takes no `banlist_version`; a pin with one is a `400`. The versions block then carries `legality_as_of` in place of the banlist version.

## Report

Follow the answer contract:

- Citations as footnotes. Render each footnote by the citation's `type`: `card` and `legality` resolve with `GET /cards/{id}`, `ban_event` with `GET /ban-events/{id}`.
- One versions block at the end, merged from every response. A Commander answer includes the banlist version, the one that decided each `banned` loss. A Brawl answer includes `legality_as_of` in its place (see `answer-contract.md`). Always include it.
- Assumptions next to the numbers they shape: the pull depth, the presentation depth, the similarity measure, and the scope value. When the top tier fills the pull, the tier assumption. When the list marked no commander, the commander you chose.

If the service is down at any step, report that. Then stop. Never answer from recall.
