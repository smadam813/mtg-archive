# Graph database fit for card and rules relationships

Research feeding the "Storage and index architecture" decision. Researched 2026-08-23 against primary sources (official docs, vendor repos, first-party APIs); each claim cites its source.

## Recommendation

**No graph database for the MVP.** The corpus is a small, curated *lookup* graph — every relationship type is known at ingest time, and every query the MVP runs is 1–2 hops deep. That is plain relational joins, which Postgres already does, alongside the pgvector index the stack already needs. A dedicated graph store (Neo4j) or a graph extension (Apache AGE) would add a second data model and a dual-write ingest for no query the done-queries require. If graph-pattern ergonomics ever become desirable, PostgreSQL 19 (GA expected Sept–Oct 2026) ships SQL/PGQ — `GRAPH_TABLE` pattern matching over the *same* relational tables, no extra store — which removes most of the remaining argument for a separate graph system.

## The corpus, sized

The whole "graph" fits in memory on any machine that can run the compose stack:

| Entity | Count | Source |
|---|---|---|
| Unique cards (non-digital, all printings collapsed) | 32,726 | Scryfall search API, `q=-is:digital&unique=cards`, `total_cards` field (fetched 2026-08-23) |
| Keyword abilities | 223 | [Scryfall catalog `keyword-abilities`](https://api.scryfall.com/catalog/keyword-abilities) (fetched 2026-08-23) |
| Keyword actions | 79 | [Scryfall catalog `keyword-actions`](https://api.scryfall.com/catalog/keyword-actions) (fetched 2026-08-23) |
| Comprehensive Rules | 9 numbered sections plus glossary; keywords defined in rules 701–702, Commander in rule 903 | [WotC rules page](https://magic.wizards.com/en/rules) (official CR downloads) |
| Curated combo variants | 106,351 | [Commander Spellbook API](https://backend.commanderspellbook.com/variants/?limit=1&count=true), `count` field (fetched 2026-08-23) |

Even a maximal edge set — every card linked to its keywords, every keyword to its defining rule, every rule cross-reference, every combo variant to its ~2–5 constituent cards — is well under a million rows. This is orders of magnitude below where graph-native storage pays for itself; the data is also near-static (on-demand re-ingest, no live writes).

## What the graph model would hold

Nodes: `Card`, `Keyword/Mechanic`, `Rule`, `ComboVariant`, `BanlistEntry`. Edges:

- `Card —HAS_KEYWORD→ Keyword` (from card data; 1 hop)
- `Keyword —DEFINED_BY→ Rule` (CR 701/702; 1 hop)
- `Rule —REFERENCES→ Rule` ("see rule 601.2" cross-references; the only genuinely recursive relation)
- `ComboVariant —USES→ Card`, `—PRODUCES→ Result` (curated, imported from Spellbook; 1 hop)
- `Card —SYNERGIZES_WITH→ Card` (derived pairwise scores; 1 hop)

The structural observation that drives the recommendation: **these edges are consumed by lookup, not discovered by traversal.** Combo lines are not found by walking synergy edges — the ecosystem's canonical combo dataset is curated and validated upstream (Commander Spellbook), and the vision's own line-finding mechanism is the goldfish engine (simulation), not graph search. Notably, Commander Spellbook itself — the largest combo database in the ecosystem, 106k variants — runs on **Django + PostgreSQL**, with no graph database anywhere in its stack ([SpaceCowMedia/commander-spellbook-backend](https://github.com/SpaceCowMedia/commander-spellbook-backend)).

## Query analysis: what each side expresses

### What the MVP actually asks, in SQL terms

| MVP query | Shape | Postgres answer |
|---|---|---|
| "Cards with keyword X, legal in identity Y under banlist B" | filter + 1-hop join | ordinary indexed joins |
| "Combos executable within this decklist / collection" | set-containment over `variant_uses(variant_id, card_id)` | `GROUP BY variant HAVING count = required count` — exactly what Spellbook does on Postgres |
| "Cards that do X" (semantic) constrained by legality | vector similarity + hard filter | pgvector `WHERE` filtering; since 0.8.0 iterative index scans expand the search automatically when filters discard too many candidates ([pgvector README](https://github.com/pgvector/pgvector)) |
| "Synergy candidates for card C" | 1–2-hop join (shared keyword / shared combo / precomputed edge) + similarity rank | join + pgvector hybrid; pgvector also documents hybrid full-text + vector patterns |
| Rule cross-reference closure ("everything rule 903 leans on") | transitive closure | `WITH RECURSIVE`, with built-in `SEARCH DEPTH\|BREADTH FIRST` ordering and `CYCLE` detection ([PostgreSQL docs, queries-with](https://www.postgresql.org/docs/current/queries-with.html)) |

Nothing in the three done-queries (commander alternatives, build-from-collection with simulated consistency, manabase fix) traverses deeper than this. The deep work — combo assembly, turn-N probabilities — is the simulator's job by design.

### What a graph database adds that SQL expresses poorly

Real, but unneeded here:

- **Variable-length / k-shortest path with per-edge predicates.** Cypher's `SHORTEST k`, `ALL SHORTEST`, `SHORTEST k GROUPS` ([Cypher manual, shortest paths](https://neo4j.com/docs/cypher-manual/current/patterns/shortest-paths/)). Weighted variants require the separate Graph Data Science library.
- **Whole-graph algorithms** — centrality, community detection, similarity, node embeddings, link prediction ([Neo4j GDS algorithms](https://neo4j.com/docs/graph-data-science/current/algorithms/)). These would matter for *discovering* synergy structure or archetype clusters — a post-MVP research feature at most, and the open-source OpenGDS build is GPLv3 ([neo4j/graph-data-science](https://github.com/neo4j/graph-data-science)).
- **Ergonomics**: multi-hop patterns read better in Cypher than as join chains. This is a readability gain, not a capability gain — and SQL/PGQ (below) closes most of it.

No MVP query needs any of these. The one recursive relation (rule cross-references) is shallow, acyclic in practice, and covered by `WITH RECURSIVE`.

### SQL/PGQ changes the long-term calculus

PostgreSQL 19 implements SQL:2023 property graph queries: `CREATE PROPERTY GRAPH` declares a read-only graph view over existing tables, and `GRAPH_TABLE (… MATCH (a IS card)-[IS has_keyword]->(k) …)` runs pattern matching over it — no extension, no second store ([PostgreSQL 19 docs, Property Graphs](https://www.postgresql.org/docs/19/ddl-property-graphs.html); [commitfest patch](https://commitfest.postgresql.org/patch/4904/)). PG 19 Beta 1 shipped June 2026 with GA expected Sept–Oct 2026; the initial version covers fixed-depth patterns, with variable-length paths expected later ([release notes](https://www.postgresql.org/docs/release/19.0/), [depesz write-up](https://www.depesz.com/2026/07/31/waiting-for-postgresql-19-sql-property-graph-queries-sql-pgq/)). If the schema keeps relationships as plain edge tables now, graph-pattern querying becomes available later by *declaring* a property graph over them — zero migration.

## Operational cost of the alternatives

### Neo4j (dedicated graph container)

- **Extra container + JVM**: Community Edition is GPLv3 ([neo4j licensing](https://neo4j.com/licensing/)); current releases require Java SE 21/25; 2 GB RAM minimum, 8 GB+ recommended for production workloads ([system requirements](https://neo4j.com/docs/operations-manual/current/installation/requirements/)). Official Docker image and Compose deployment are documented ([Docker docs](https://neo4j.com/docs/operations-manual/current/docker/)).
- **.NET client**: mature — official `Neo4j.Driver` v6 on NuGet, supports .NET 8/9/10 ([.NET driver manual](https://neo4j.com/docs/dotnet-manual/current/)). Not the bottleneck.
- **The real cost is dual-store ingest**: every re-ingest writes cards/rules/banlists twice, keeps two schemas in sync, and legality — which the vision requires to be structurally unbypassable — would exist in two places. Backups, transactions, and the "no query path returns an illegal candidate" property all fragment.
- Neo4j has had native HNSW vector indexes since 5.11 (cosine/Euclidean, ≤4,096 dims) ([vector index docs](https://neo4j.com/docs/cypher-manual/current/indexes/semantic-indexes/vector-indexes/)), so a Neo4j-only architecture is *possible* — but it trades away pgvector's richer filtering/hybrid story and the relational integrity the legality principle depends on.

### Apache AGE (graph extension in the same Postgres)

- Apache-2.0 Postgres extension providing openCypher via a `cypher()` function callable from SQL, mixable with SQL in CTEs and joins ([AGE manual](https://age.apache.org/age-manual/master/intro/overview.html)); supports PG 11–18, current release v1.8.0, actively developed ([apache/age](https://github.com/apache/age)).
- No extra container, and because queries run through plain SQL, **any Postgres client including Npgsql works** — the dedicated community .NET client ([Allison-E/pg-age](https://github.com/Allison-E/pg-age), `ApacheAGE` 1.0.0 on NuGet, May 2024, ~25 stars) is immature but also unnecessary.
- Still not free: AGE stores graph data in its own vertex/edge tables using `agtype`, so it is a second data model inside the same database — dual ingest again, and no foreign keys between graph entities and the relational card records that legality lives on.

### Postgres + pgvector (the default candidate)

- pgvector 0.8.x, PG 13+: HNSW and IVFFlat indexes, six distance metrics, filtered and iterative index scans, hybrid full-text + vector search, ACID/WAL/replication with the rest of the data ([pgvector README](https://github.com/pgvector/pgvector)). One container, one schema, one ingest, one backup.

## Bottom line

A graph database would add expressive power the MVP never exercises, at the cost of the property the vision treats as non-negotiable: a single structural store where legality is derived once and unbypassable. Model card↔mechanic↔rule, combo→card, and synergy relationships as ordinary edge tables in Postgres; use joins and `WITH RECURSIVE` for the rare closure query; keep embeddings in pgvector.

**Revisit triggers** (any of these reopens the question — and Apache AGE or PG 19 SQL/PGQ, not Neo4j, is the cheap first step because both stay inside the one Postgres container):

1. A product feature needs *discovery* traversal — e.g., "find an interaction chain from A to B through owned cards" as a query rather than a simulation.
2. Whole-graph analytics (centrality/community detection over synergy edges, archetype clustering) become a roadmap item.
3. Rule/mechanic cross-reference queries grow past what recursive CTEs express readably — at which point PG 19's `GRAPH_TABLE` over the existing edge tables is the first resort.
