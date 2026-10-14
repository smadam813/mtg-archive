# Retrieval eval

`queries.json` holds the curated queries (65 card-search, 13 rules-search)
that gate the embedding-model choice. The runner scores them through the
real service path — `POST /cards/semantic-search` and `POST /rules/search`
over HTTP, so the service-side query prefixes are exercised — never a
direct-to-Ollama shortcut.

The Commander set gates every format, because format filtering is exact and order-preserving (`CardSearchTests` pins it).

The glossary pulls page in the Commander popularity order: `edhrec_rank`, nulls last, then name, then oracle id (`PopularityRankTests` pins it). A pulled card with no `edhrec_rank` merges in name order, not pull order.

## Metrics

- **nDCG@10** — linear gain (`grade / log2(rank + 1)`, the trec_eval
  convention); the ideal ranking is the fixture's own grades (0–3 for cards;
  a rules expectation is binary).
- **recall@30** — the share of expected ids among the top 30 cards or chunks.

Both are reported per query, per surface (`card_search`, `rules_search`),
overall, and for the `archetype_vocab: true` subset against the rest
(`card_search_archetype_vocab` vs `card_search_other`), so a human can judge
whether appending Oracle Tags into Card documents helps or backfires. Each
per-query entry keeps its top 10 hits for that review.

A rules expectation names a subrule (`903.10a`); a hit cites the chunk that
holds it (`903.10`), which is what the scorer matches.

## Running it

The runner is the xunit test `RetrievalEvalLiveTests` in
`tests/MtgArchive.Tests`, behind the `Category=retrieval-eval` trait. The
project's default test filter excludes it, so a plain `dotnet test` never
runs it, in CI or anywhere else. IDE test explorers ignore that filter, so
run the eval from the CLI. It needs a live, ingested service:
`docker compose up`, then `POST /ingest` and wait for
`GET /ingest/runs/latest` to report the run finished.

```sh
dotnet test tests/MtgArchive.Tests --filter Category=retrieval-eval --logger "console;verbosity=detailed"
```

`MTG_ARCHIVE_URL` overrides the service address (default
`http://127.0.0.1:8080`). `MTG_ARCHIVE_EVAL_LABEL` adds a suffix to the
result file name for an ablation cell (`-strip-reminder-text`). The runner
writes `MTG_ARCHIVE_EVAL_NOTE` into the file as `note`. Use the note to name
the composition and the compose overlay the re-embed ran on. The embedding
state alone does not say either.

A run against a service whose `embedding_state` mismatches configuration
fails outright with the disabled reason. It is never scored as zeros.

Results land in `results/<date>-<model>.json`, the model tag sanitized for
filenames (`qwen3-embedding-0.6b`; a colon is not a legal filename character
on Windows). The unsanitized tag is inside as `embedding_state.model_tag`.
Commit the results: they are the gate inputs.

## Two-pass model comparison

The gate, applied by the follow-up human issue: **`qwen3-embedding:0.6b`
must beat `nomic-embed-text` on overall nDCG@10 or the fallback wins the
model slot.** Every run states the rule; the runner prints the side-by-side
table and the verdict whenever the newest result for each model exists.

Switching models is service configuration plus a forced ingest. Compose is
never edited.

1. Pass 1 with the configured model (`qwen3-embedding:0.6b` by default):
   `curl -X POST http://127.0.0.1:8080/ingest`, wait for the run, run the eval.
2. Restart the service with `Embedding__ModelTag=nomic-embed-text`. On the
   host dev loop (`docker compose up postgres ollama`):
   `Embedding__ModelTag=nomic-embed-text dotnet run --project src/MtgArchive`.
   In compose: `docker compose stop service` then
   `docker compose run --rm --service-ports -e Embedding__ModelTag=nomic-embed-text service`.
   The service pulls the model at startup.
3. Force a re-embed:
   `curl -X POST http://127.0.0.1:8080/ingest -H 'content-type: application/json' -d '{"force":true}'`
   and wait for `GET /ingest/runs/latest` to finish. Until then the semantic
   endpoints answer disabled and the eval fails.
4. Run the eval again. The comparison table and verdict print.
5. Restart with the winning tag and force ingest once more.

`nomic-embed-text`'s 768-dim vectors are zero-padded to the `vector(1024)`
columns by the embedding client. Zero dimensions leave cosine distance
untouched, and queries pad the same way, so the pass measures the native
model.

## Document-composition cells

`Embedding__Composition` selects how the service embeds the oracle text
line of a card document. `verbatim` is the frozen default.
`strip-reminder-text` is cell A: parentheticals removed. `verbalize-symbols`
is cell B: cell A plus mana and tap symbols as words. The served text never
changes. A non-default composition joins the prefix-set version, so a switch
closes the gate and the next ingest run re-embeds the whole corpus. Run a
cell like a model switch:

1. Restart the service with the setting.
2. Trigger ingest on the GPU overlay and wait for the run to finish.
3. Run the eval with the label and the note set.
4. Restart the service with the frozen setting and ingest again.

| composition | prefix-set version |
|---|---|
| `verbatim` | `9D84B9513202418B` |
| `strip-reminder-text` | `9451A13348857A45` |
| `verbalize-symbols` | `6CEB9DA145C880B1` |

## Glossary pass

The runner scores the `archetype_vocab: true` subset a second time
through the plugin's archetype glossary,
`plugin/mtg-archive/references/archetype-glossary.md` (ADR
0009). It reads that one file and copies nothing. For each query it applies
the file's own rule.

It pulls one `POST /cards/search` page per mapped tag
or type (`limit` 60, popularity order). It then sends one
`POST /cards/semantic-search` with the phrasing in place of the term. The
pulled cards come first in the merge.

The result file reports the glossary
pass as `card_search_archetype_vocab_expanded` in `summary` and as
`expanded_queries` per query. Each entry carries the term, the tags and
types pulled, the query sent, and the merged top 10. The bare `card_search`
numbers do not change, so the ADR 0007 gate reads the same fields. A row
change in the glossary is what this pass measures. Re-run it and commit the
result.

## Regression harness

Re-run on any model-tag, prefix-set, or document-composition change and
commit the new result. On demand only, never CI.
