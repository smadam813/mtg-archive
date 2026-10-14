---
paths:
  - "tests/MtgArchive.Tests/**/*.cs"
---

# Tests

- Every test lives flat in `tests/MtgArchive.Tests/`, no subfolders, one class per file named for its subject (`BracketClassificationTests.cs`). Fixture files go under `fixtures/<area>/`; the csproj copies them to output.
- A test that touches Postgres carries `[Collection(PostgresCollection.Name)]`. `PostgresFixture` starts the suite's one Testcontainers `pgvector/pgvector:pg18` container (Docker must be running) and migrates its default database once; that database is shared, so a test that must own its data calls `CreateDatabaseAsync`. The fixture's `max_connections=500` override is load-bearing: per-test databases and idle pooled connections exhaust the default mid-suite.
- Every HTTP test hosts through `ArchiveApp` (a `WebApplicationFactory` with `AutoMigrate` off, fake Scryfall, CR, and Ollama sources, a per-host temp work dir). No test reaches the network or a real Ollama; swap a service via `configureServices`, override configuration via `settings`.
- The csproj default filter excludes the live retrieval eval (`Category=retrieval-eval`, `RetrievalEvalLiveTests`). A `--filter` passed on the command line replaces that default, so a broad filter pulls it back in; the eval fails without an ingested service at `MTG_ARCHIVE_URL`. The eval runs on demand only, per `eval/retrieval/README.md`.
- The property suites `NoIllegalCandidatesTests` and `NoUnownedCandidatesTests` drive `IndexFuzzHarness`, which discovers every POST whose body derives from `CandidateQueryRequest`. A new candidate endpoint needs no registration there; a suite's self-check proves the harness still catches a deliberately bypassing probe. `POST /commander-alternatives` does not derive from the base (its role is fixed); `CommanderAlternativesTests` covers its boundary properties directly.
- Test compositions, not only leaves. A goal test that passes for one `on_battlefield` leaf must also pass for an `all_of` whose leaves hold on different turns.
- A pilot test builds a `PilotView` by hand. Start from the `View` helper in `GoalAwareHoldPilotTests` and the cards in `TestDeckCards`. Give each card a cost the lands in the view can pay, or the cast never appears among the actions.
