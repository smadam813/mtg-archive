# Trigger gate

The trigger gate checks that the plugin skills fire on the done queries. Each case sends one query to a new Claude Code session with the plugin loaded. Each case has these two graders:

- `graders/skill.md`: the session invokes the expected skill.
- `graders/service-path.md`: a shell call reaches the expected service path at the address the answer contract states (`MTG_ARCHIVE_URL` or its default).

The tier-4 case adds `graders/hold-run.md`: a shell call posts `play_policy` `goal-aware-hold`. The gate grades tool calls only. It cannot grade the answer text. Read the answer by hand for the separate hold result, the policy echo, and the baseline caveat. The case also has `abstracted-run.md` and `interactive-run.md`, so all three runs show in the calls.

The gate does not grade answer quality. The acceptance records in `eval/acceptance/` do that.

| Case | Query | Skill | Service path |
|---|---|---|---|
| `done-query-1-banned-commander` | Done-query 1 and the done-query 1 fixture deck | `commander-swap` | `/commander-alternatives` |
| `done-query-2-turn-one-pithing-needle` | Done-query 2 against the imported collection | `build-deck` | `/cards/search`, `/cards/semantic-search`, `/tier1-report`, or `/simulate` |
| `done-query-3-esper-manabase` | Done-query 3 and the done-query 3 fixture deck | `build-deck` | `/tier1-report` |
| `done-query-3-paraphrase-color-screwed` | A plain complaint about blue mana and the same deck | `build-deck` | `/tier1-report` |
| `done-query-3-paraphrase-cant-cast` | A plain complaint about blue spells and the same deck | `build-deck` | `/tier1-report` |
| `brawl-query-1-competitive-brawl-swap` | BQ1 and the BQ1 fixture deck | `commander-swap` | `/commander-alternatives` |
| `brawl-query-2-standard-brawl-build` | BQ2 against the imported BQ2 collection | `build-deck` | `/cards/search`, `/cards/semantic-search`, `/tier1-report`, or `/simulate` |
| `brawl-query-3-brawl-formats-deck-check` | BQ3 and the BQ3 fixture deck | `deck-check` | `/deck-verdict` |
| `tier-4-hold-run-deck-check` | A tier-4 question on an inline deck with response cards | `deck-check` | `/simulate`, with three runs |

The tier-4 case holds its deck inline; no fixture feeds it. The other prompts copy the decklists from `eval/acceptance/done-query-1/decklist.json`, `eval/acceptance/done-query-3/decklist.txt`, `eval/acceptance/brawl-query-1/deck.txt`, and `eval/acceptance/brawl-query-3/deck.txt`. When a fixture changes, change the prompts too.

`grader-samples.json` holds match and no-match strings for the tier-4 `input_match` regexes. `/verify` runs `.claude/skills/verify/grader-check.py` against it. Add a sample when you add a grader.

## Precondition

Start the compose stack. Import a collection. The done-query 3 collection (`eval/acceptance/done-query-3/collection.csv`) covers every Commander case. The BQ2 case needs the BQ2 collection (`eval/acceptance/brawl-query-2/collection.csv`), which holds its commander. Apply the address line from `references/answer-contract.md`, then check that the service answers:

```sh
curl -sS "$MTG_ARCHIVE_URL/healthz"
```

Caution: native Windows has no sandbox, so the gate blocks the Bash tool there. Run the gate under WSL2 or Linux with `bubblewrap` and `socat` installed. With WSL2 mirrored networking, the service on the Windows host answers on `127.0.0.1:8080`.

## Run

The full run scores every grader, including the optional ones, so it needs WSL2 or Linux. Run it from the repo root:

```sh
claude plugin eval plugin/mtg-archive --ablation none --allow-tools Bash --trust-plugin
```

`--allow-tools Bash` lets the skills call the service with curl. `--ablation none` skips the run without the plugin, because that run cannot fire a plugin skill. Each case runs three times, and Haiku is the judge model. Keep both defaults. The run exits 1 if a case scores below 1.0.

Each case sets `model: sonnet`, because the done-query 3 failure was on Sonnet 5.5.

Without `--allow-tools Bash`, only `graders/skill.md` can pass. The tier-4 run graders need Bash, so they are optional like the service-path graders. That run is the gate: every skill grader must pass on every run. The service-path graders are optional, because they need WSL2 or Linux. The gate command drops the flag:

```sh
claude plugin eval plugin/mtg-archive --ablation none --trust-plugin
```

The gate session starts in an empty directory and loads no `CLAUDE.md`. Thus it cannot repeat a session that starts inside this repo.

The run writes results to `evals/results/`. Git ignores that directory.

## Not a CI step

The trigger gate is local. CI has no Claude CLI and no live service. Run the gate after each change to a skill `description`. Record the per-case scores in the PR body.
