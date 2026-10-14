---
name: archive-admin
description: "Import a collection, trigger an ingest, or check ingest and corpus status. Use when the user asks to update, import into, or inspect the archive itself."
---

# archive-admin

This skill keeps the archive current. It does three chores: import the collection, trigger an ingest, and report the archive's status. It reports what the service returns. It does not judge it.

Read `../../references/answer-contract.md` first. It states how to reach the service, what to do when the service is down, and how to report. Apply its `MTG_ARCHIVE_URL` default line before the first request. Every request below uses that address. If the service does not answer, tell the user to start the compose stack and stop.

The service omits a null field from every response. Where this file says a field is absent, do not look for a literal `null`.

## Which chore

| The user asks to | Do |
|---|---|
| Import, load, or replace their collection | [Import the collection](#import-the-collection) |
| Show what the archive thinks they own | [Show the collection](#show-the-collection) |
| Update, refresh, or re-ingest the archive | [Trigger an ingest](#trigger-an-ingest) |
| Check on a running or finished ingest | [Report an ingest run](#report-an-ingest-run) |
| Ask how fresh the archive is, or which versions it serves | [Report the corpus state](#report-the-corpus-state) |

## Import the collection

The route is `PUT /collection`. The body has two fields:

- `format`: `manabox_csv` for a ManaBox export, `list` for a plain-text list (one `quantity name` per line).
- `content`: the file text, verbatim.

**Warning: the import is a full replace.** The service deletes the stored collection and writes the new rows in one transaction. Rows absent from the new file are gone after the import. State this to the user before you send the request. Do not send a partial file to "add" cards.

Read the file with `jq` so that `jq` escapes the content field:

```sh
jq -n --arg format manabox_csv --rawfile content ./collection.csv \
  '{ "format": $format, "content": $content }' \
  | curl -sS -X PUT "$MTG_ARCHIVE_URL/collection" \
      -H 'content-type: application/json' --data-binary @-
```

### Report the import

A 2xx response carries the counts and the quarantine report together:

| Field | Meaning |
|---|---|
| `total_rows` | Rows parsed from the file |
| `imported_rows` | Rows that resolved to a card |
| `quarantined_rows` | Rows that did not resolve |
| `distinct_cards` | Distinct cards now owned |
| `total_quantity` | Total copies now owned |
| `quarantine` | The quarantine report: one entry per row that did not resolve |

Report all five counts. Then render the quarantine report verbatim, as a table, one row per entry:

| `line_number` | `name` | `reason` | `candidate_oracle_ids` |
|---|---|---|---|

The `reason` is `no_match` or `ambiguous`. `no_match` means no card carries the name (misscans, tokens, name drift). `ambiguous` means the name belongs to several cards, and `candidate_oracle_ids` lists them. The field is absent for `no_match`.

A quarantine inside a 2xx is not an error. The import succeeded for every other row.

Never omit an entry. Never summarize the list. Never pick a candidate for an ambiguous row. Never re-send a row under a corrected name. Fixing the file is the user's job. Say so.

If the response is a 400 problem with type `urn:mtg-archive:problem:empty-collection-import`, no row resolved and the stored collection is unchanged. The `quarantine` extension member still lists every row. Render it the same way.

This response carries no versions block.

## Show the collection

The route is `GET /collection`:

```sh
curl -sS "$MTG_ARCHIVE_URL/collection"
```

The response has `cards` and `import`. Each card has `oracle_id`, `name`, and `quantity`, in name order. `import` holds the last import's `format`, `imported_at`, and its three row counts. `import` is absent before the first import. Report the import metadata. Then report the cards the user asked about. If they named none, report the count.

## Trigger an ingest

The route is `POST /ingest`. The body has one optional field, `force`. Send `true` only when the user asks to re-run on unchanged sources. Without it, an unchanged-source run records a no-op.

```sh
curl -sS -X POST "$MTG_ARCHIVE_URL/ingest" \
  -H 'content-type: application/json' -d '{ "force": false }'
```

Expect `202 Accepted` with a body of `{ "run_id": N }` and a `Location` header pointing at the run. Ingest is single-flight with no queue. If a run is active, the service returns that run's id instead of starting a new one. Report the active run's id. Do not retry the trigger. Then report the run as below.

## Report an ingest run

The routes are `GET /ingest/runs/{id}` and `GET /ingest/runs/latest`:

```sh
curl -sS "$MTG_ARCHIVE_URL/ingest/runs/latest"
```

A 404 problem means no run with that id, or no run at all. Report that.

The response carries `run_id`, `status` (`running`, `succeeded`, `failed`), `force`, `no_op`, `started_at`, `finished_at`, `phases`, and `report`. It carries no versions block.

### While it runs

`status` is `running` and `report` is absent. Render `phases` in order, one line per phase: `phase`, `status` (`pending`, `running`, `succeeded`, `failed`, `skipped`), and `error` when set. Tell the user to ask again later. Do not poll in a loop.

### When it is done

`report` is the ingest report. When `no_op` is true, the sources were unchanged and the run skipped every phase. Say so. The user may ask for a forced run. Otherwise render every item of the report verbatim:

| Item | Render as |
|---|---|
| `new_cards`, `changed_cards` | Counts |
| `drift_detections` | A table, one row per entry: `kind`, `name`, `oracle_id`, the entry's own fields, and its `resolution` verbatim |
| `cr_version_found` | The CR effective date found, or "none" |
| `stale_cards` | A table of `oracle_id`, `name` |
| `collection_rows_pointing_at_stale_cards` | Its `rows` as a table of `oracle_id`, `name`, `quantity`. When `populated` is false, say the run failed before it measured this item |
| `unmatched_search_lands`, `unmatched_enters_tapped_lands`, `unmatched_opponent_sources` | Tables of `oracle_id`, `name` |
| `unprofiled_cards` | The count, then a table of `oracle_id`, `name`, `face`, `sentence`. The list can hold thousands of rows: show the first 20 and say how many more there are. Show the rest when the user asks |
| `response_cards` | Two tables of `oracle_id`, `name`, `face`: the entries with `profiled` true, then the entries with `profiled` false, each with its `sentence`. Give each count. Show the first 20 of each list and say how many more there are |
| `brawl_legality` | One block per Brawl format, as in the Format section below |
| `skipped_popularity_rows` | A table, as in the Format section below |
| `errors` | Each string verbatim |

Drift is a mismatch between a curated file and Scryfall's current state. Its kinds are `banlist` and `game_changers`. Every entry is report-only: the curated file stays authoritative.

Never filter, resolve, or act on a drift entry. Never edit a curated file from this skill. Acting on drift is a human's job: append the dated event to the curated file with its provenance and re-ingest. Say so under the table.

An `unprofiled_cards` entry is report-only too. The face has a sentence that mentions draw, search or add outside the closed card-feature templates. XMage still plays the card; the pilot reads only the features the templates proved. Never edit a curated file for it.

A `response_cards` entry is report-only too. A profiled entry is a counterspell or a protection card that interactive disruption can use. An unprofiled entry has a sentence that mentions an answer outside the closed response families, so an answered rate is a lower bound. Never edit a curated file for it.

A `failed` run still carries the counts measured before the failure. Report the failed phase's `error` verbatim. Recovery is running the ingest again.

### Format

`brawl_legality` holds one entry per Brawl format: `brawl`, `competitive_brawl`, `standard_brawl`. Each entry has `format`, `legality_as_of` (the Scryfall date of its legality), and the counts `legal`, `banned`, and `not_legal`. The counts are cards in the deck. Render the counts as one line per format.

`status_changes` lists the cards whose status changed since the prior ingest: a ban, an unban, or a rotation out of `standard_brawl`. Render it as a table of `oracle_id`, `name`, `from`, `to`. An empty list means no change. Say so.

Drift is a Commander term only. A Brawl format has no drift entry, because Scryfall is the source of truth for Brawl legality. A status change is not drift. Never act on it.

`skipped_popularity_rows` lists the popularity export rows with stats that the ingest did not load. Render it as a table of `format`, `name`, `seat`, `reason`. The `name` is the export's, verbatim. The `seat` is `main` for a deck row and `commander` for a commander row.

The `reason` is `no_match` (no card in the format by that name), `ambiguous` (more than one card), or `duplicate` (an earlier row filled that card's seat). An empty list means every row loaded. Say so. A skipped row is report-only. Never edit the export for it.

## Report the corpus state

The route is `GET /corpus-state`:

```sh
curl -sS "$MTG_ARCHIVE_URL/corpus-state"
```

This is the archive's versions at rest: what the service serves every answer from right now. Render it as five blocks:

1. **Versions.** The `versions` block: `banlist_version`, `cr_effective_date`. Render it per the answer contract. An absent field means no ingest has loaded that axis. Say "not ingested" for it. This route does not fill `gc_snapshot` or `bracket_spec_version`. Do not report those two axes from here.
2. **Embedding state.** `embedding_state`: `model_tag`, `dimensions`, `prefix_set_version`. Absent means no re-embed has recorded the model that produced the stored vectors.
3. **Last ingest per phase.** `last_phase_runs`: phase name to `run_id` and `finished_at`. A phase absent from the map has never succeeded. List the phases in the order the ingest runs them: `fetch_validate`, `card_upsert`, `source_profile_derive`, `card_feature_derive`, `cr_refresh`, `banlist_replay`, `legality_rebuild`, `popularity_load`, `bracket_load`, `re_embed`.
4. **Freshness.** `freshness`: `scryfall_updated_at`, `cr_effective_date`, `cr_text_sha256`, and `curated_file_hashes` (file path to hash). These are the source stamps the last successful run recorded. The next ingest compares against them and records a no-op when nothing changed.
5. **Popularity.** `popularity_exports`: one entry per format with a popularity export, with `format`, `window_start`, `window_end`, and `file_sha256`. Render one line per format for all four formats with its popularity axes. A format with an entry has `popularity_source` `untapped` and `popularity_as_of` its `window_end`. A format without one has `popularity_source` `edhrec`, the proxy. Its `popularity_as_of` is the Scryfall Oracle Cards date, which this route does not show. Say "Scryfall date" for it.

Answer a "how fresh is the archive" question from `freshness` and `last_phase_runs`. Answer a "which banlist / CR" question from `versions`. Do not answer either from `GET /ingest/runs/latest`; a run can fail or no-op without changing what is at rest.

## What this skill never does

- It never filters, resolves, or acts on a quarantine entry or a drift entry. It renders them and stops.
- It never edits a curated file, a collection file, or a data file.
- It never retries a trigger while a run is active, and never polls in a loop.
- It never answers a status question from recall. A down service means "start the compose stack".
