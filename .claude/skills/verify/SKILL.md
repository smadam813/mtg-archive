---
name: verify
description: Run the PR bar for this repo (format check, build, sidecar image, test suite) and report failures. Use before opening a PR, after finishing an issue, or when asked to verify or run checks; pass an optional test filter as the argument.
argument-hint: "[test filter, e.g. FullyQualifiedName~BracketClassificationTests]"
---

Run from the repo root, in this order, and stop at the first stage that fails. Measured on a warm Windows host with Docker Desktop: format 23 s, build 11 s, image 2 s (cached), tests 2 min. A cold image build takes 5 to 10 minutes, so plan about 10 to 13 minutes for a first run:

1. `docker info` succeeds. The test suite starts a Testcontainers Postgres and fails, not skips, without a daemon. If Docker is down, report that and stop.
2. `dotnet format --verify-no-changes`. On failure run `dotnet format`, read `git diff -w` (the analyzer pass can rewrite code, not just layout; revert any rewrite that is not correct), then re-check. Report the files it changed.
3. `dotnet build`. Warnings are fine; errors stop the run.
4. Build the XMage sidecar image to this checkout's own tag, not the shared `ci` tag, which other worktrees rebuild:

   ```bash
   tag="mtg-archive-xmage:$(basename "$PWD" | tr 'A-Z' 'a-z' | tr -cd 'a-z0-9_.-')"
   docker build --target runtime -t "$tag" engine/xmage
   ```

   The build runs the shim's `mvn test` and the determinism self-test. The first build takes 5 to 10 minutes; Docker caches it after that. It runs after the .NET build so that a format or compile error surfaces first. On failure, report the failing stage and its last `[ERROR]` or `SELFTEST` lines.
5. Run the tests with `MTG_XMAGE_IMAGE="$tag"`, so the XMage tests use the image from step 4. With no filter when `$ARGUMENTS` is empty: `MTG_XMAGE_IMAGE="$tag" dotnet test`. With a filter: `MTG_XMAGE_IMAGE="$tag" dotnet test tests/MtgArchive.Tests --filter "($ARGUMENTS)&Category!=retrieval-eval"`. A filter on the command line replaces the csproj default, and the live retrieval eval fails without an ingested service. The parentheses keep a `|` in the filter from escaping the exclusion.

An XMage test that fails with "image ... is stale" ran on an image from before a wire protocol change. Rebuild with step 4 and rerun.

After step 5, run `python3 .claude/skills/verify/grader-check.py` (or `python`). It compiles each eval grader regex in `plugin/mtg-archive/evals` and tests it against the samples in `evals/grader-samples.json`. A failure means a regex lost a character; fix the grader. Add a sample for each new grader.

Then run `bash .claude/skills/verify/prose-check.sh`. It lists sentences of more than 25 words in the changed docs, the glossary and the ADRs (CLAUDE.md writing style). It is advisory: shorten what you wrote, and leave older text alone.

Some gates sit outside the suite and need the live stack, so do not run them here. List the changed paths with `git diff --name-only main` and `git status --short`, and name each gate the change owes:

- A change under `engine/xmage` (XMage tag, patch, or shim) owes the parity gate in `eval/parity/README.md`.
- A change to the pilot policy (`*Pilot*.cs`, `PlayPolicies.cs`, `MulliganPolicies.cs` in `src/MtgArchive/Simulation/Engine/`) owes a `parity-baseline --image mtg-archive-xmage` run, per `eval/parity/README.md`.
- An XMage tag move owes the throughput gate in `eval/engine/README.md`. A pilot change, or a shim change on the interactive path, owes it only if it adds round trips; check and say which.
- A change to the embedding model tag, a prefix, or the embedded document text owes the retrieval eval in `eval/retrieval/README.md`.

Report: each stage as pass or fail, then every failing test by name with its assertion message, then the fix you propose, then each owed gate. Green means all five stages pass with no filter. A filtered run checks a change but does not meet the PR bar; report it as filtered.
