# Commander play-rate data for the staple cut

**Question**: What sources provide Commander play-rate/popularity data (EDHREC or
alternatives) usable to define the "defined set of staples" cut? Cover: APIs or exports,
licensing/ToS for self-hosted use, granularity (per-card play rates, per-commander breakdowns),
and whether on-demand refresh is feasible. Feeds the "Staple set definition" decision.

**Researched:** 2026-08-23. All claims verified directly against the cited source on that date.

## Summary

Use **Scryfall's `edhrec_rank`** as the primary play-rate signal. It is EDHREC-derived, ships in
Scryfall's daily bulk exports and REST API, is explicitly licensed for free use in Magic software,
and refreshes on demand. Do **not** scrape EDHREC directly: its ToS prohibits automated queries
and redistribution, and its JSON endpoints are unofficial. The one thing Scryfall does not carry
is inclusion *percentages* — it carries an ordinal rank — but a top-N-by-rank cut intersected with
functional categories (ramp/tutor/draw/selection/fast mana) is sufficient to define the staple
set, and the cut is curated anyway because DSL implementation effort, not data, is the bottleneck.

## Sources evaluated

### 1. Scryfall (recommended primary)

- **Granularity:** Each card object carries `edhrec_rank` — "This card's overall
  rank/popularity on EDHREC. Not all cards are ranked" (Integer, nullable) — and
  `game_changer` — "True if this card is on the Commander Game Changer list" (Boolean,
  nullable). Source: [Card object docs](https://scryfall.com/docs/api/cards). Verified live:
  `GET /cards/named?exact=Sol+Ring` returns `edhrec_rank: 1`, `game_changer: false`.
- **Ranked pull on demand:** `/cards/search?q=legal:commander&order=edhrec&dir=asc` returns all
  Commander-legal cards ordered by EDHREC rank (verified live 2026-08-23: 31,830 cards; Sol Ring,
  Command Tower, Arcane Signet at ranks 1–3). One paginated search yields a top-N staple list.
- **Exports:** "Scryfall provides daily exports of our card data in bulk files" (gzipped JSONL);
  bulk file URLs "change their timestamp each day, and can be fetched programmatically." The
  `oracle_cards` file (one object per Oracle ID) is the natural ingest target. The `oracle_tags`
  bulk file (community Tagger data, includes functional tags such as ramp/tutor) can assist the
  category intersection. Source: [Bulk data docs](https://scryfall.com/docs/api/bulk-data),
  verified against the live `/bulk-data` endpoint.
- **Licensing:** "As part of the Wizards of the Coast Fan Content Policy, Scryfall provides our
  card data and image database free of charge for the primary purpose of creating additional
  Magic software, performing research, or creating community content." Constraints: no paywalling
  Scryfall data, don't "just copy … or proxy Scryfall data — your software must create additional
  value," anonymous access if you have accounts. Source: [API overview](https://scryfall.com/docs/api).
  Self-hosted use is squarely within this grant.
- **Rate limits / refresh:** hard limits — `/cards/search` 2/sec; most other methods 10/sec;
  `*.scryfall.io` file origins (bulk downloads) have **no** rate limits. A `User-Agent` and
  `Accept` header are required on every API request. Sources:
  [Rate limits](https://scryfall.com/docs/api/rate-limits), [API overview](https://scryfall.com/docs/api).
  On-demand refresh: trivially feasible (daily bulk re-download, or a single ranked search).
- **Caveat:** `edhrec_rank` is an **ordinal rank**, not an inclusion percentage, and Scryfall does
  not state how often it re-syncs the rank from EDHREC. A rank supports "top N" cuts but not
  "played in ≥X% of eligible decks" cuts.

### 2. EDHREC (upstream source — read, don't scrape)

- **Granularity is the best available:** the site's own JSON (e.g.
  `https://json.edhrec.com/pages/cards/sol-ring.json`) exposes per-card `num_decks` /
  `potential_decks` (verified 2026-08-23: Sol Ring 8,130,874 of 9,757,907 color-eligible decks
  over the past two years ≈ 83% inclusion), per-commander pages with per-card inclusion and
  `synergy`, salt scores, and top-cards lists (`pages/top/year.json`). Synergy is defined in the
  [EDHREC FAQ](https://edhrec.com/faq) as inclusion in this commander's decks minus inclusion in
  other decks of the same color identity.
- **Freshness:** "EDHREC collects deck data from Archidekt, Moxfield, and Scryfall. We collect
  data daily from these pages and new data is reflected most often on pages within a few days."
  ([FAQ](https://edhrec.com/faq))
- **No official API.** The `json.edhrec.com` endpoints are undocumented internals used by the
  site's own frontend; community wrappers (e.g. pyedhrec, mightstone) all note they are
  unofficial and can change without notice.
- **ToS blocks self-hosted use.** The [terms](https://edhrec.com/terms) grant "a non-transferable,
  non-exclusive, revocable, limited license to access the Site solely for your own personal,
  noncommercial use," and prohibit: using "software or automated agents or scripts … to generate
  automated searches, requests, or queries to the Site"; copying/reproducing/distributing any part
  of the Site "unless otherwise indicated"; and accessing the Site "to build a similar or
  competitive website."
- **Conclusion:** consume EDHREC data only via licensed redistributors (Scryfall, MTGJSON). If
  inclusion percentages ever become a hard requirement, ask EDHREC for permission rather than
  scraping.

### 3. MTGJSON (alternative carrier of the same signal, plus saltiness)

- **Granularity:** card model fields `edhrecRank` ("The card rank on EDHRec," since v4.5.0) and
  `edhrecSaltiness` ("The card saltiness score on EDHRec," since v5.2.1), present in the Atomic
  files (AtomicCards et al.). Source: [MTGJSON card data model](https://mtgjson.com/data-models/card/card-atomic/).
- **Licensing / cost / refresh:** "MTGJSON is free and open source under the MIT License." Builds
  are daily: "Builds kick off at 1:00AM EST and go live at 9:00AM EST." Source:
  [MTGJSON FAQ](https://mtgjson.com/faq/).
- **Position:** redundant with Scryfall for rank; worth it only if we want `edhrecSaltiness` or
  already ingest MTGJSON for other reasons.

### 4. EDHTop16 (complementary cEDH/tournament lens)

- **What it is:** aggregator of competitive EDH tournament results, sponsored by Topdeck.gg
  ([about page](https://edhtop16.com/about)); legacy codebase MIT-licensed
  ([EDH-Top-16/edhtop16-legacy](https://github.com/EDH-Top-16/edhtop16-legacy)).
- **API:** public GraphQL endpoint at `https://edhtop16.com/api/graphql` with introspection
  enabled (verified live 2026-08-23). Schema includes `Commander`, `Card`, `Entry`,
  `CommanderCardStats { conversionRate, topCuts, totalEntries }` — i.e. per-commander,
  per-card **tournament** play and conversion stats.
- **Position:** measures cEDH tournament metagame, not general Commander play. Wrong population
  for the MVP staple cut, but the right source later if bracket/power-level work needs a
  competitive-play signal.

### 5. Moxfield / Archidekt (raw decklist sources — not needed)

- **Moxfield:** no public API; access to the unofficial `api.moxfield.com` is restricted and
  legitimate tools are told to request permission via support@moxfield.com (see
  [Moxfield feedback thread](https://moxfield.nolt.io/1431) and community integrations such as
  [mpc-autofill #265](https://github.com/chilli-axe/mpc-autofill/issues/265)).
- **Archidekt:** undocumented but informally open API; staff say on the
  [Archidekt forum](https://archidekt.com/forum/thread/2832338) that EDHREC already pulls their
  data this way and they welcome others, with throttling, a link-back request, and the caveat
  they will lock it down if abused.
- **Position:** these are deck *sources*, not play-rate aggregates. EDHREC already aggregates
  both daily ([EDHREC FAQ](https://edhrec.com/faq)), so building our own aggregation duplicates
  that pipeline for no MVP benefit.

## Recommendation

1. **Define the staple cut from Scryfall:** top-N Commander-legal cards by `edhrec_rank`
   (via `order=edhrec` search or the daily `oracle_cards` bulk file), intersected with the
   functional categories the Effect DSL targets (ramp, tutor, draw, selection, fast mana) —
   using Scryfall `oracle_tags` and/or hand curation for the category test. Persist the cut as a
   dated, versioned list (like the banlist), since rank drifts and DSL coverage should be stable.
2. **N is a budget knob, not a data question.** The binding constraint is DSL implementation
   effort per card; rank only orders the queue. An ordinal rank is therefore fully sufficient,
   and the missing inclusion-percentage granularity costs us nothing for this decision.
3. **Refresh:** re-ingest with the normal Scryfall daily bulk pull; on-demand refresh is one
   rate-limited search away. No scraping infrastructure needed.
4. **Do not scrape EDHREC** (ToS: personal noncommercial only, no automated queries, no
   redistribution). If percentages or per-commander synergy ever become requirements, request
   permission from EDHREC or reassess MTGJSON/EDHTop16.
5. **Record EDHTop16's GraphQL API** as the future source for the competitive/bracket axis; it is
   out of scope for the staple cut.
