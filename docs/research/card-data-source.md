# Card data source: Scryfall vs MTGJSON

**Question**: Which bulk card data source should the corpus ingest — Scryfall bulk data, MTGJSON, or both?

**Answer: Ingest Scryfall bulk data as the sole source. Do not build an MTGJSON pipeline; optionally use MTGJSON's precomputed `leadershipSkills` / `hasAlternativeDeckLimit` / `isGameChanger` fields as a one-off test fixture to cross-check our own derivations.**

All facts below were verified against primary sources on 2026-08-23. Live values (file sizes, timestamps, sample cards) were fetched directly from `api.scryfall.com` and `mtgjson.com/api/v5` on that date.

---

## Why Scryfall alone

1. **Scryfall is upstream.** MTGJSON aggregates: "Data is collected from a variety of sources, including Magic: The Gathering's own Gatherer, API's like Scryfall and TCGplayer, and many more" ([MTGJSON FAQ](https://mtgjson.com/faq/)). MTGJSON cards even carry `identifiers.scryfallOracleId` (verified on Black Lotus in `LEA.json`). For oracle-text fidelity there is no case where MTGJSON is more correct than its own source.
2. **The fields MTGJSON adds are derivations we must own anyway.** The vision requires ingest to derive legality attributes once, per (card, role, banlist version). Neither source models roles or banlist history — that logic is ours regardless — so MTGJSON's precomputed booleans buy a second pipeline, not a saved derivation. They remain useful as a cross-check (see below).
3. **Scryfall ships data MTGJSON has no equivalent of** that maps directly onto the Index layer: the **Oracle Tags** bulk file — community functional tags ("removal, ramp, or draw") keyed by `oracle_id`, with stable tag UUIDs and a parent/child hierarchy ([Tags docs](https://scryfall.com/docs/api/tags)). This is a ready-made seed for "cards that do X" retrieval.
4. **Smaller, streamable files and a real delta mechanism** (details below): 23.4 MB gzipped JSONL for the whole oracle-level corpus, and a `/cards/manifest` endpoint listing per-card `data_updated_at` timestamps.

---

## Comparison

### Oracle-text fidelity

- **Scryfall**: `oracle_text` on the card object; multiface cards are one object with a `card_faces` array; `oracle_id` is "consistent across reprinted card editions, and unique among different cards with the same name" ([Card objects](https://scryfall.com/docs/api/cards)). Scryfall maintains this text as its core product.
- **MTGJSON**: `text` — "The rules text of the card" ([Card (Atomic)](https://mtgjson.com/data-models/card/card-atomic/)) — aggregated from upstream sources including Scryfall (FAQ, above). Same content, one hop later.

### Color identity

- **Scryfall**: `color_identity` (type Colors) on every card ([Card objects](https://scryfall.com/docs/api/cards)).
- **MTGJSON**: `colorIdentity` — "A list of all the colors found in `manaCost`, `colorIndicator`, and `text` properties" ([Card (Atomic)](https://mtgjson.com/data-models/card/card-atomic/)).
- Equivalent. Scryfall also has `produced_mana` (colors of mana a card could produce), useful for the manabase-analysis destination question.

### Commander-relevant legality fields

- **Scryfall** `legalities`: an object with an entry for **every** format — "Possible legalities are `legal`, `not_legal`, `restricted`, and `banned`" ([Card objects](https://scryfall.com/docs/api/cards)). Live Sol Ring fetch (2026-08-23) enumerated 23 keys including `commander`, `paupercommander`, `duel`, `oathbreaker`, `brawl`, `predh`. Absence of legality is explicit (`not_legal`).
- **MTGJSON** `legalities`: 23 documented format properties including `commander`, `duel`, `paupercommander`, `oathbreaker`, `predh` ([Legalities model](https://mtgjson.com/data-models/legalities/)). Values verified in `LEA.json`: `"Legal"` / `"Banned"` / `"Restricted"`, and **formats where a card is not legal are omitted** (Black Lotus: `{"commander":"Banned","duel":"Banned","legacy":"Banned","oathbreaker":"Banned","oldschool":"Restricted","predh":"Banned","vintage":"Restricted"}` — no `standard` key). Scryfall's explicit `not_legal` is the cleaner input for a deterministic legality writer.
- **Game Changers** (bracket axis, deliberately not legality per the vision): both have it — Scryfall `game_changer` ("True if this card is on the Commander Game Changer list", [Card objects](https://scryfall.com/docs/api/cards)); MTGJSON `isGameChanger` (verified `True` on Demonic Tutor in `LEA.json`).
- **Neither source publishes banlist history.** Both are current-state snapshots. The dated banlist version history the corpus requires must be built by us — by diffing `legalities` across dated bulk snapshots (mechanism below).

### Printings vs. the abstract card

- **Scryfall**: the split is a file choice. **Oracle Cards** bulk file = "one Scryfall card object for each Oracle ID" (the abstract card); **Default Cards** = "every card object on Scryfall in English" (printings). Join via `oracle_id`; `prints_search_uri` pages all reprints ([Bulk data](https://scryfall.com/docs/api/bulk-data), [Card objects](https://scryfall.com/docs/api/cards)).
- **MTGJSON**: **AtomicCards** (abstract, keyed by name, with a `printings` set-code list) vs **AllPrintings** (per-set printings) ([All files](https://mtgjson.com/downloads/all-files/)).
- Equivalent modeling. For the MVP, ingest Oracle Cards; add Default Cards later only if collection tracking needs printing-level identity.

### Keyword / mechanic tagging

- **Scryfall**: `keywords` — "An array of keywords that this card uses, such as 'Flying' and 'Cumulative upkeep'" — plus the **Oracle Tags** bulk file: community-maintained functional-role tags ("removal, ramp, or draw"), tag objects with stable UUID `id`, `parent_ids`/`child_ids` hierarchy, and `taggings` keyed by `oracle_id`. Caveats from the docs: community-maintained, moderated but not guaranteed error-free; use tag `id` not `slug` as the stable reference ([Tags docs](https://scryfall.com/docs/api/tags)).
- **MTGJSON**: `keywords` — "A list of keywords found on the card" — plus a `Keywords.json` vocabulary file ([Card (Atomic)](https://mtgjson.com/data-models/card/card-atomic/), [All files](https://mtgjson.com/downloads/all-files/)). No functional-role tagging.

### Update cadence and on-demand refresh / deltas

- **Scryfall**: "Scryfall provides daily exports of our card data in bulk files … URLs for files change their timestamp each day, and can be fetched programmatically. … Bulk data is only collected once every 12-24 hours." Also: "Updates to gameplay data (such as card names, Oracle text, mana costs, etc) are much less frequent. If you only need gameplay information, downloading card data once per week or right after set releases would most likely be sufficient" ([Bulk data](https://scryfall.com/docs/api/bulk-data)).
  - **Delta mechanism**: no delta files, but (a) the `/bulk-data` catalog reports `updated_at` per file — cheap freshness check; (b) `/cards/manifest` returns a paginated list of every card with `data_updated_at` and `image_updated_at` — "you can use the /cards/manifest method to check for anything that has changed on Scryfall" — then fetch fresh objects from the card API (verified live: 116,700 entries). In practice, at 23.4 MB compressed, re-downloading Oracle Cards and diffing against the stored snapshot is simpler than the manifest and is the same operation that produces **dated banlist events** for the version history.
- **MTGJSON**: daily builds — "Builds kick off at 1:00AM EST and go live at 9:00AM EST" ([FAQ](https://mtgjson.com/faq/)); `Meta.json` reported `{"date":"2026-08-23","version":"5.3.0+20260823"}` live. Freshness check via `Meta.json`; integrity via per-file `.sha256`. No delta files; full re-download.

### File formats and sizes (measured 2026-08-23)

| Source / file | Format | Compressed | Uncompressed |
|---|---|---|---|
| Scryfall Oracle Cards | JSONL, gzip | 23.4 MB | — |
| Scryfall Default Cards | JSONL, gzip | 73.9 MB | — |
| Scryfall All Cards (all languages) | JSONL, gzip | 374 MB | — |
| Scryfall Rulings (by `oracle_id`) | JSONL, gzip | 5.1 MB | — |
| Scryfall Oracle Tags | JSONL, gzip | 5.6 MB | — |
| MTGJSON AtomicCards | JSON (+xz/gz/bz2/zip) | 25.2 MB (xz) | 159.5 MB |
| MTGJSON AllPrintings | JSON, SQL, SQLite, PostgreSQL | 96.5 MB (xz) | 657 MB (JSON), 676 MB (SQLite) |

Scryfall sizes from the live `/bulk-data` endpoint (`compressed_size`); MTGJSON from `Content-Length` HEAD requests. Scryfall bulk files are "gzipped JSONL (JSON Lines)" designed to be streamed without full decompression ([Bulk data](https://scryfall.com/docs/api/bulk-data)). MTGJSON's SQLite/PostgreSQL exports are its main format advantage — irrelevant here since ingest writes our own store.

### Licensing / ToS for self-hosted use

- **Scryfall**: "Scryfall provides our card data and image database free of charge for the primary purpose of creating additional Magic software, performing research, or creating community content" under the WotC Fan Content Policy. Conditions: no paywalling the data, no implying Scryfall endorsement, no using it for non-Magic games, and "You may not simply repackage, republish, or proxy Scryfall data. Your software must create additional value for end-users." API requests require an accurate `User-Agent` and an `Accept` header; bulk downloads need no authentication ([API overview](https://scryfall.com/docs/api)). A self-hosted knowledge base with derived legality, indexing, and simulation is squarely "additional Magic software" with added value.
- **MTGJSON**: "MTGJSON is free and open source under the MIT License" ([FAQ](https://mtgjson.com/faq/)). The underlying card text remains Wizards of the Coast copyright under the Fan Content Policy in both cases.
- No blocker either way for self-hosted use.

---

## Field map: deriving per-(card, role, banlist-version) legality from Scryfall

| Derived attribute | Scryfall input | Cross-check (MTGJSON) |
|---|---|---|
| Banned status (in the 99) | `legalities.commander` ∈ {`legal`, `not_legal`, `restricted`, `banned`} | `legalities.commander` ∈ {`Legal`, `Banned`, `Restricted`, absent} |
| Banlist version history | **Not provided by either source.** Diff `legalities` across dated Oracle Cards snapshots at ingest; each transition becomes a dated banlist event | — |
| Commander eligibility (role = commander) | Derive: `type_line` contains `Legendary` + `Creature`, or `oracle_text` contains "can be your commander" (planeswalkers, Backgrounds, etc.) | `leadershipSkills.commander` — "The formats the card is legal to be a commander in" |
| Copy limits | Singleton is a format rule, not card data. Exceptions: `Basic` supertype in `type_line`; `oracle_text` "A deck can have any number of cards named …" | `hasAlternativeDeckLimit` — "If the card allows a value other than 4 copies in a deck" |
| Color identity (commander + 99 constraint) | `color_identity` | `colorIdentity` |
| Game Changer (bracket axis, not legality) | `game_changer` | `isGameChanger` |
| Companion (role = companion) | `keywords` contains `Companion`; restriction text in `oracle_text` | `keywords` |

The three derivations (eligibility, copy-limit exceptions, banlist events) are small, testable text/type-line rules. **Recommended use of MTGJSON:** download `AtomicCards.json.xz` once as a test fixture and assert our derived `commander eligibility` and `copy-limit` flags agree with `leadershipSkills` / `hasAlternativeDeckLimit`, joined on `identifiers.scryfallOracleId`. That gets MTGJSON's value without a second ingest pipeline.

## Recommended ingest shape

1. **Oracle Cards** (`oracle_cards` jsonl.gz, ~23 MB) — the abstract-card corpus.
2. **Rulings** (~5 MB, joined on `oracle_id`) — corpus text for retrieval.
3. **Oracle Tags** (~6 MB, joined on `oracle_id`) — seed vocabulary for "cards that do X"; treat as advisory (community data), reference tags by UUID.
4. Refresh on demand or ~weekly: check `/bulk-data` `updated_at`; if newer, download, diff `legalities` by `oracle_id` against the stored snapshot, append dated banlist events, rewrite derived legality attributes.
5. Defer **Default Cards** (printings) until collection tracking needs printing-level identity.

## Sources

- Scryfall bulk data docs: https://scryfall.com/docs/api/bulk-data (fetched 2026-08-23)
- Scryfall live bulk catalog: https://api.scryfall.com/bulk-data (fetched 2026-08-23)
- Scryfall card object docs: https://scryfall.com/docs/api/cards (fetched 2026-08-23)
- Scryfall API overview / data-use terms: https://scryfall.com/docs/api (fetched 2026-08-23)
- Scryfall tags docs: https://scryfall.com/docs/api/tags (fetched 2026-08-23)
- Scryfall live card sample (legality keys): https://api.scryfall.com/cards/named?exact=Sol+Ring (fetched 2026-08-23)
- Scryfall change manifest: https://api.scryfall.com/cards/manifest (fetched 2026-08-23)
- MTGJSON all files: https://mtgjson.com/downloads/all-files/ (fetched 2026-08-23)
- MTGJSON Card (Atomic) model: https://mtgjson.com/data-models/card/card-atomic/ (fetched 2026-08-23)
- MTGJSON Legalities model: https://mtgjson.com/data-models/legalities/ (fetched 2026-08-23)
- MTGJSON LeadershipSkills model: https://mtgjson.com/data-models/leadership-skills/ (fetched 2026-08-23)
- MTGJSON FAQ: https://mtgjson.com/faq/ (fetched 2026-08-23)
- MTGJSON live metadata: https://mtgjson.com/api/v5/Meta.json (fetched 2026-08-23)
- MTGJSON live sample (value casing, omission semantics, `isGameChanger`, `scryfallOracleId`): https://mtgjson.com/api/v5/LEA.json (fetched 2026-08-23)
- File sizes: `compressed_size` from the Scryfall bulk catalog; `Content-Length` HEAD requests against https://mtgjson.com/api/v5/ (2026-08-23)
