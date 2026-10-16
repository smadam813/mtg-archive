# API conventions

The service is plain HTTP+JSON on port 8080 — no MCP facade, no Swagger UI, no
batch endpoints, no URL version prefix (the consumer ships in this repo and
compose stack; they co-evolve atomically). Everything is synchronous except
ingest. Every endpoint PR follows these conventions and keeps `/openapi.json`
current — it is the canonical machine-readable contract.

## Casing

snake_case everywhere on the wire — one casing from corpus to wire
(`oracle_id`, `banlist_version`). `Api/WireJson.cs` configures it globally:
property names, dictionary keys, and enum values.

## Verbs

- `POST` for computed queries (searches, simulate, tier-1, alternatives,
  bracket).
- `GET` for reference fetches.
- `PUT /collection` for full-replace import.
- `POST /ingest` to trigger a run.

## Versioned inputs

Every version parameter (`banlist_version`, bracket spec version, GC
snapshot) is optional and defaults to latest. The
response echoes the versions actually used in a top-level `versions` block
(`Api/VersionsBlock.cs`). Pinning an unknown version is a 4xx
`application/problem+json`, never a silent fallback. Resolve pins through
`Api/VersionParameter.cs`.

## Citations

One structured shape everywhere (`Api/Citation.cs`):

```json
{ "type": "card|rule|ban_event|legality|snapshot", "id": "...", "label": "..." }
```

Every citation `id` must be fetchable from a reference endpoint — no
prose-only citations. A `legality` citation is the legality status of one
card in a Brawl format or Standard (ADR 0012). Its id is the oracle id, so it resolves
through `GET /cards/{oracle_id}`, whose row for that format holds the
Scryfall key, the Scryfall URL, and the as-of date. Its label states
what the key says: "`<key> <key status> as of <yyyy-MM-dd>`", for example
"competitivebrawl banned as of 2026-10-03". The key never holds
`not_eligible`. A `not_eligible` row adds the fact the service derives, for
example "brawl legal as of 2026-10-03; not eligible as commander".

Card detail lists per-role legality for all five formats, one row per
(format, role). The `banlist_version` pin applies to the Commander rows
only.

## Candidate queries

Every candidate-producing query (card search) is a
`POST` whose body derives from `Api/CandidateQueryRequest.cs`. The body
carries the inputs the legality boundary needs:

- `format`: optional, `commander` by default. The `versions` block echoes
  it. An unknown format is a 400 that names the value.
- `banlist_version`: optional, latest by default, Commander only. A Brawl
  format or Standard has no banlist version, so a pin with one is a 400. The `versions`
  block then carries `legality_as_of`, the Scryfall bulk `updated_at`, in
  its place.
- `role`: `commander | companion | main`, default `main`.
- `color_identity`: optional deck identity as letters `W U B R G`; `[]`
  admits colorless cards only.
- `collection_scope`.

The handler starts from
`CandidateQuery.ResolveAsync` and composes its own predicates — and the
pgvector KNN — onto the returned `legal_candidates` query, so the whole
thing runs as one SQL statement; no other path reaches candidate card rows
(ADR 0001). The response carries a top-level `candidates` array, each
element citing its card under `card` (`{ type: card, id: oracle_id, label }`),
plus `versions`. Each candidate carries `popularity_rank`, its 1-based place in
the format's popularity order: the commander order under role `commander`,
the card order otherwise. `versions` names the order's
`popularity_source` (`edhrec` or `untapped`) and `popularity_as_of`. Join
`CandidateQuery.Popularity` to sort or tie-break by it; no route orders by
`edhrec_rank` itself. The no-illegal-candidates harness
(`tests/MtgArchive.Tests/IndexFuzzHarness.cs`) discovers index endpoints by
the request base type and fuzzes every one of them, so an endpoint is under
the property test the moment it maps.

`POST /commander-alternatives` is the one exception to the base type: its
role is fixed to commander and its color identity is the retention metric,
not a filter, so the harness's fuzzed `role` and `color_identity` would
contradict its contract. It still starts from `CandidateQuery.ResolveAsync`
and its own tests cover the boundary properties. It takes `format` and
`banlist_version` with the same rules as the base type.

## Collection scope

`collection_scope: true|false`, default `false`, a per-request body field on
every candidate-producing query (`Api/CandidateQueryRequest.cs`) — never a
header or global toggle. The service never infers it. Under scope,
availability = min(copy limit, owned quantity), composed in the same SQL
statement through `ArchiveDbContext.AvailableCopies`; an endpoint that
reports it does so as `available` on each candidate, and only under scope.

## Decklists

Every deck route takes the one `decklist` body and resolves its names
through `Simulation/DecklistResolver.cs`. A name that does not resolve and
starts with `A-` resolves again without the prefix. The response then
carries a top-level `name_rewrites` array: each entry is the name as sent
and the `card` citation it resolved to. With no rewrite the member is
absent. `POST /decklist-parse` turns Arena export text or a plain
`<qty> <name>` list into the `decklist` body plus `notes`. It reads no
database and resolves no names.

The deck routes serve Commander and the three Brawl formats. Standard has
card legality only so far. `POST /deck-verdict`, `/simulate`, `/tier1-report`,
and `/commander-alternatives` answer it with the 400 `unserved-format` problem.

`POST /bracket-classification` takes `format` like the other deck routes
and echoes it in `versions`. Brackets apply to Commander only (ADR 0012), so
a Brawl format or Standard is the 400 `commander-only` problem, checked before any
other field.

## Errors

RFC 9457 `application/problem+json` with domain extension members in
snake_case (e.g. `unresolved_names`) — see `Api/Problems.cs`. Author
extension members through `Problems.WithExtension`, which normalizes keys to
snake_case (the framework's ProblemDetails serializer writes extension keys
verbatim, outside `WireJson`'s policy). Only "the request can't be answered"
is an error: quarantine rows and drift are report content inside 2xx
responses.

## Token economy

Lean defaults, revisited only on measured need:

- Exemplar lines capped at 3.
- Rulings only on card detail.
- No sparse-fieldset machinery.
- No batch endpoints.
