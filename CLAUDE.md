# CLAUDE.md

MTG Archive is a self-hosted MTG Commander and Arena Brawl knowledge base: a corpus, an index, and a simulation engine. A .NET 10 minimal API (`src/MtgArchive`) serves it from one Postgres + pgvector database and an XMage sidecar (`engine/xmage`, ADR 0011); agent skills consume it. Ollama serves the embeddings (`qwen3-embedding:0.6b`, ADR 0007).

## Agent skills

### Plugin

The five user-facing skills live in `plugin/mtg-archive/skills/`; their shared conventions live in `plugin/mtg-archive/references/`. `.claude-plugin/marketplace.json` makes the repo the marketplace. Repo-internal skills stay under `.claude/skills/`.

After a change to a skill `description`, re-run the trigger eval in `plugin/mtg-archive/evals/README.md` without the Bash grant. The skill graders are the gate. Record the scores in the PR body.

Tests pin skill text: `*SkillTests.cs`, `PluginScaffoldTests`, and `ArchetypeGlossaryTests` assert strings in each SKILL.md and reference. Run them after editing either.

### Domain docs

Single-context: `GLOSSARY.md` and `docs/adr/` at the repo root. Read the ADRs that touch your area. Use the glossary's terms. If your output contradicts an ADR, say so.

## Writing style

Keep each sentence in docs, the glossary, the ADRs, and this file to 25 words or fewer. `/verify` runs `prose-check.sh`, which flags longer added sentences.

## Workflow

An issue is takeable when it is open, unassigned, and every blocker is closed. Assign yourself, branch `issue/<number>-<slug>` off `main`, and open one PR with `Fixes #<number>`. Keep tests green, including the property suites the issue names. A human reviews and merges. If an acceptance criterion contradicts an ADR or `GLOSSARY.md`, raise it on the issue instead of picking a side.

Before a PR, run `/verify`; a filtered run does not meet the bar. Plain `dotnet test` needs Docker and a local `mtg-archive-xmage:ci` image, because Testcontainers never pulls it. `/verify` builds a per-checkout tag instead and passes it in `MTG_XMAGE_IMAGE`.

The retrieval eval in `eval/` is a gate: re-run it on any model-tag, prefix-set, or document-composition change. See `eval/retrieval/README.md`.

The parity gate in `eval/parity/` is a gate too: re-run it on any XMage tag, patch, or shim change. Re-run `parity-baseline` after each pilot change too, with `--image mtg-archive-xmage`; the `:ci` default can be stale.

The throughput gate in `eval/engine/` re-runs on each XMage tag move and on each pilot or shim change that adds round trips.

The XMage source is not on disk; fetch one file at the pinned commit with `gh api`, per `engine/xmage/README.md`. On Git Bash, set `MSYS_NO_PATHCONV=1` before Docker or sparse-checkout work there.

Schema changes are generated with `/new-migration`; files under `src/MtgArchive/Migrations/` are never hand-edited, except a user-approved `migrationBuilder.Sql` DDL call. New routes follow `/new-endpoint`.
