---
name: factory
description: Run the software factory. Takes a feature through spec, build, four parallel reviews, approval, and a local merge to main, tracked in factory/board.json. Use only when the user types /factory.
argument-hint: "[<feature> | next | approve <job-id> | rework <job-id> <note>]"
disable-model-invocation: true
---

# The factory

This session is the orchestrator. It runs the loop, spawns every agent, and is the only writer of `factory/board.json`. Agents never see this conversation; they get context from files.

```text
feature → spec-writer → builder ⇄ 4 reviewers (security, ux, ui, code) → approver → merge to main
                                  any CHANGES: next round (max 2)      ESCALATE → needs-human ← /factory approve
```

Run one orchestrator at a time. Two sessions running /factory would overwrite each other's board.json.

## Paths

Set `ROOT` to `git rev-parse --show-toplevel` in the main checkout, written with forward slashes (for example `C:/git/mtg-archive`). Every path you give an agent is absolute.

| What | Path |
|---|---|
| Board | `ROOT/factory/board.json` |
| Backlog | `ROOT/factory/backlog.md` |
| Job folder | `ROOT/factory/jobs/<id>/` |
| Round folder | `ROOT/factory/jobs/<id>/round-<n>/` |
| Worktree | `ROOT/.worktrees/<id>` on branch `factory/<id>` |

Everything under `factory/` lives in the main checkout, never in a worktree. Agents write only their own file.

## Arguments

| `$ARGUMENTS` | Do |
|---|---|
| empty | Resume every job whose stage is not `merged` or `needs-human`, in board order. |
| `next` | Take the first `- [ ]` line in backlog.md. Create the job, and once its job folder exists, change that line to `- [x]` with ` → <id>` appended. Run it. Empty backlog: say so and stop. |
| `approve <id>` | Job must be `needs-human`. Append `Approved by a human via /factory approve on <date>.` to decision.md. Go to **Merge**. |
| `rework <id> <note>` | Job must be `needs-human`. Run the **Rework** step. |
| anything else | It is a feature. Create the job and run it. |

## Board

`factory/board.json`:

```json
{ "jobs": [ { "id": "003-rate-limiting", "title": "Rate-limit the search endpoints",
  "feature": "<the feature text, verbatim>", "branch": "factory/003-rate-limiting",
  "stage": "review", "round": 2, "updated": "2026-10-15T14:02:00Z",
  "reviews": [
    { "security": "CHANGES", "ux": "PASS", "ui": "PASS", "code": "PASS" },
    { "security": "pending", "ux": "PASS", "ui": "pending", "code": "PASS" } ] } ] }
```

- `stage` is one of `spec`, `build`, `review`, `approval`, `merging`, `needs-human`, `merged`.
- `reviews` has one entry per round. Each verdict is `pending`, `PASS`, or `CHANGES`.
- Write it atomically on every change: Write `board.json.tmp`, then `mv -f factory/board.json.tmp factory/board.json` in Bash.
- Update it on each stage change and as each reviewer's verdict lands, so the dashboard fills in live. Set `updated` each time.
- If board.json is missing, start from `{ "jobs": [] }`.

## Create a job

1. Id: `NNN-slug`. NNN is one more than the highest number among board jobs and `factory/jobs/*`, zero-padded to three digits. The slug is two to four kebab-case words from the feature.
2. `mkdir -p ROOT/factory/jobs/<id>`.
3. Add the job to the board: `stage: "spec"`, `round: 0`, `reviews: []`, `title` as a short phrase, `feature` verbatim.

## The loop

Run the steps in order. Every agent is spawned with the Agent tool, `subagent_type` set to the agent name. Each runs in the background; wait for its completion notification. Never predict an agent's result.

**Missing file rule.** When an agent finishes without writing its file, or the file's first line is not a valid `VERDICT:`, run it once more. A second miss sends the job to needs-human.

### 1. Spec (stage `spec`)

Skip this step when `jobs/<id>/spec.md` exists. Spawn `spec-writer` with the feature text, `ROOT`, and the output path `ROOT/factory/jobs/<id>/spec.md`.

When it lands, read the spec's `## Conflicts` section. Anything but `None` sends the job to needs-human, with the conflict as the reason. CLAUDE.md says a human picks the side when a criterion contradicts an ADR or the glossary.

### 2. Build (stage `build`)

1. Ensure the worktree: if `ROOT/.worktrees/<id>` is missing, run `git worktree add .worktrees/<id> -b factory/<id> main` from ROOT. If the branch already exists, drop `-b`.
2. Increment `round` to N, `mkdir -p` the round folder, and append `{ "security": "pending", "ux": "pending", "ui": "pending", "code": "pending" }` to `reviews`.
3. Round 1: spawn `builder` with name `builder-<id>`. Later rounds: SendMessage to `builder-<id>`. If that builder no longer exists, spawn a fresh one with the same name; it picks up from the files and the worktree.
4. The prompt gives: the job id, the worktree path, the spec path, the round folder, the previous round folder if N > 1, and the output path `round-N/build.md`. Say whether `round-N/rework.md` exists.

### 3. Review (stage `review`)

Spawn the four reviewers in one message so they run in parallel: `reviewer-security`, `reviewer-ux`, `reviewer-ui`, `reviewer-code`. Each prompt gives the worktree path, the spec path, `round-N/build.md`, and its output path `round-N/review-<name>.md`. Names are `security`, `ux`, `ui`, `code`.

As each finishes, read the first line of its file and set its verdict on the board.

**WAIT FOR ALL.** The round is done only when all four files exist in the round folder. Never message the builder mid-round. Apply the missing file rule to each reviewer.

Then decide:
- All four PASS: go to **Approve**.
- Any CHANGES, and `round-N/rework.md` exists or N ≥ 2: needs-human, reason "round N still has CHANGES from <names>".
- Any CHANGES otherwise: go to **Build** for round N + 1.

### 4. Approve (stage `approval`)

Spawn `approver` with the job folder, the worktree path, and the output path `ROOT/factory/jobs/<id>/decision.md`. Read its first line. `VERDICT: APPROVE` goes to **Merge**. `VERDICT: ESCALATE` moves the job to needs-human; the approver already wrote the reason.

### 5. Merge (stage `merging`)

From ROOT, in Git Bash with `MSYS_NO_PATHCONV=1`:

1. `git rev-parse --abbrev-ref HEAD` must print `main`. If not, needs-human: "main checkout is not on main".
2. `git -C .worktrees/<id> rebase main`. On a conflict, `git -C .worktrees/<id> rebase --abort` and go to needs-human, naming the conflicting files.
3. In the worktree, run the PR bar in `.worktrees/<id>/.claude/skills/verify/SKILL.md`, all five stages, no filter. Use a 600000 ms timeout; a cold image build can take ten minutes. On any failure, needs-human, with the failing stage and test names.
4. `git merge --ff-only factory/<id>` in ROOT.
5. `git worktree remove .worktrees/<id>`, then `git branch -d factory/<id>`.
6. Stage `merged`.

The merge is local. Never push. If `git merge-base --is-ancestor factory/<id> main` already holds when you resume, skip to step 5.

### Needs-human

Whenever you move a job to `needs-human` yourself, write `jobs/<id>/decision.md` as `VERDICT: ESCALATE`, a blank line, then the reason. Then report the job id and reason to the user and go on to the next job.

### Rework

For `/factory rework <id> <note>` on a needs-human job:

1. Let N be `round + 1`. Write `<note>` to `round-N/rework.md` under a `# Rework` heading.
2. If spec.md is missing, run **Spec** first. If the job stopped at spec (round 0), round 1 is an ordinary round that also reads rework.md.
3. Run **Build** for round N, then **Review**. A rework round does not count toward the two-round cap. If it still has a CHANGES, the job goes to needs-human again.

## Resume

For `/factory` with no argument, continue each unfinished job from what is on disk, not from memory:

| stage | Do |
|---|---|
| `spec` | spec.md exists: go to Build. Else spawn spec-writer. |
| `build` | `round-N/build.md` exists: go to Review. Else message or respawn `builder-<id>` for round N. |
| `review` | Read each existing review file into the board. Spawn only the reviewers whose file is missing, then wait for all. |
| `approval` | decision.md written after the last build.md: act on it. Else spawn approver. |
| `merging` | Run Merge from step 1. |

## Report

When the run ends, list each job you touched with its final stage. For needs-human jobs, give the reason and the two commands: `/factory approve <id>` and `/factory rework <id> <note>`. Mention the dashboard: `python -m http.server 8000 -d factory`, then open `http://localhost:8000/dashboard.html`.
