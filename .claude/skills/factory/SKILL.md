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

The shell's working directory persists between commands. So run git as `git -C "ROOT" …` or `git -C "ROOT/.worktrees/<id>" …`, and give every file command an absolute path.

## The PR bar

The PR bar is defined once, under "The PR bar" in `ROOT/.claude/agents/builder.md`. Merge runs the same checks in the worktree.

At merge the bar is check-only. If `dotnet format` would change a file, that is a failure; never fix it there. A worktree the bar leaves dirty is a failure too.

## Arguments

| `$ARGUMENTS` | Do |
|---|---|
| empty | Resume every job whose stage is not `merged` or `needs-human`, in board order. |
| `next` | Take the first line of backlog.md that starts with `- [ ]`. Create the job. Once its job folder exists, change that line to `- [x]` with ` → <id>` appended. Run it. Empty backlog: say so and stop. |
| `approve <id>` | The job must be `needs-human` and have a build: `git -C "ROOT" rev-list --count main..factory/<id>` prints 1 or more. Otherwise say so and suggest `/factory rework`. Record the approval in decision.md (see **Decision history**). Go to **Merge**. |
| `rework <id> <note>` | The job must be `needs-human`. Run the **Rework** step. |
| anything else | It is a feature. Create the job and run it. |

## Board

`factory/board.json`:

```json
{ "jobs": [ { "id": "003-rate-limiting", "title": "Rate-limit the search endpoints",
  "feature": "<the feature text, verbatim>", "branch": "factory/003-rate-limiting",
  "stage": "review", "round": 2, "updated": "2026-10-15T14:02:00Z",
  "builder_agent": "<agent id or name>", "head": null,
  "reviews": [
    { "security": "CHANGES", "ux": "PASS", "ui": "PASS", "code": "PASS" },
    { "security": "pending", "ux": "PASS", "ui": "pending", "code": "PASS" } ] } ] }
```

- `stage` is one of `spec`, `build`, `review`, `approval`, `merging`, `needs-human`, `merged`.
- `reviews` has one entry per round. Each verdict is `pending`, `PASS`, or `CHANGES`.
- `builder_agent` is how SendMessage reaches this job's builder. `head` is the commit Merge fast-forwards main to.
- Write it atomically on every change. Write `ROOT/factory/board.json.tmp`, then run `mv -f "ROOT/factory/board.json.tmp" "ROOT/factory/board.json"`.
- On Windows the rename fails while the dashboard server is reading board.json. It holds the file for milliseconds, so rerun the `mv` until it succeeds, up to five tries.
- Update it on each stage change and as each reviewer's verdict lands, so the dashboard fills in live. Set `updated` each time.
- If board.json is missing, start from `{ "jobs": [] }`.

## Create a job

1. Id: `NNN-slug`. NNN is one more than the highest number among board jobs and `factory/jobs/*`, zero-padded to three digits. The slug is two to four kebab-case words from the feature.
2. `mkdir -p "ROOT/factory/jobs/<id>"`.
3. Add the job to the board: `stage: "spec"`, `round: 0`, `reviews: []`, `title` as a short phrase, `feature` verbatim.

## The loop

Run the steps in order. Every agent is spawned with the Agent tool, `subagent_type` set to the agent name. Each runs in the background; wait for its completion notification. Never predict an agent's result.

**Missing file rule.** An agent may finish without writing its file. A review or decision file may have a first line that is not a valid `VERDICT:` line. Either way, run that agent once more. A second miss sends the job to needs-human.

### 1. Spec (stage `spec`)

If `jobs/<id>/spec.md` is missing, spawn `spec-writer` with the feature text, `ROOT`, and the output path `ROOT/factory/jobs/<id>/spec.md`.

Then read the spec's `## Conflicts` section, whether you just wrote it or found it on disk. Anything but `None` sends the job to needs-human, with the conflict as the reason. CLAUDE.md says a human picks the side when a criterion contradicts an ADR or the glossary.

Skip that check when `round-1/rework.md` exists. **Rework** writes it only after a spec exists, so it is the human's ruling on that spec's conflict.

### 2. Build (stage `build`)

1. Start round N = `round + 1` in one board write: `stage: "build"`, `round: N`, and `{ "security": "pending", "ux": "pending", "ui": "pending", "code": "pending" }` appended to `reviews`. Then `mkdir -p` the round folder.
2. Ensure the worktree. If `ROOT/.worktrees/<id>` is missing and branch `factory/<id>` exists, run `git -C "ROOT" worktree add .worktrees/<id> factory/<id>`. If neither exists, run `git -C "ROOT" worktree add -b factory/<id> .worktrees/<id> main`.
3. Round 1: spawn `builder`. If the Agent tool takes a name, name it `builder-<id>`. Store that name, or else the returned agent id, as `builder_agent`. Later rounds: SendMessage to `builder_agent`. If that builder no longer exists, spawn a fresh one the same way; it picks up from the files and the worktree.
4. The prompt gives the job id, the worktree path, the spec path, and the round folder. It also gives the previous round folder if N > 1, and the output path `round-N/build.md`. Say whether `round-N/rework.md` exists.

### 3. Review (stage `review`)

Spawn the four reviewers in one message so they run in parallel: `reviewer-security`, `reviewer-ux`, `reviewer-ui`, `reviewer-code`. Each prompt gives the worktree path, the spec path, `round-N/build.md`, and its output path `round-N/review-<name>.md`. Names are `security`, `ux`, `ui`, `code`.

As each finishes, read the first line of its file and set its verdict on the board.

**WAIT FOR ALL.** The round is done only when all four files exist in the round folder. Never message the builder mid-round. Apply the missing file rule to each reviewer.

Then decide:
- All four PASS: go to **Approve**.
- Any CHANGES and N ≥ 2: needs-human, reason "round N still has CHANGES from <names>". A rework round after a build always has N ≥ 2, so it lands here too.
- Any CHANGES and N = 1: go to **Build** for round 2.

### 4. Approve (stage `approval`)

Spawn `approver` with the job folder, the worktree path, and the output path `ROOT/factory/jobs/<id>/decision.md`. Read its first line. `VERDICT: APPROVE` goes to **Merge**. `VERDICT: ESCALATE` moves the job to needs-human; the approver already wrote the reason.

### 5. Merge (stage `merging`)

Set `MSYS_NO_PATHCONV=1` in Git Bash. A failing command sends the job to needs-human with its output, unless the step says otherwise.

0. If the job's `head` is set and `git -C "ROOT" merge-base --is-ancestor <head> main` succeeds, the merge already happened. Skip to step 6.
1. `git -C "ROOT" rev-parse --abbrev-ref HEAD` must print `main`. If not, the reason is "main checkout is not on main".
2. `git -C "ROOT/.worktrees/<id>" status --porcelain` must print nothing. If not, the reason is "uncommitted changes in the worktree", with the list.
3. Run `git -C "ROOT/.worktrees/<id>" rebase main`. On a conflict, run `git -C "ROOT/.worktrees/<id>" rebase --abort`. The reason names the conflicting files.
4. Run the PR bar in the worktree, check-only. Use a 600000 ms timeout; a cold image build can take ten minutes. On a failure, the reason gives the failing stage and test names. Then check `status --porcelain` again, as in step 2.
5. Store `git -C "ROOT/.worktrees/<id>" rev-parse HEAD` as the job's `head`. Run `git -C "ROOT" merge --ff-only <head>`. It fails when main moved or uncommitted edits in the main checkout block it. Either way, the reason is git's message.
6. Set stage `merged`. Then clean up, skipping what is already gone: `git -C "ROOT" worktree remove --force .worktrees/<id>`, then `git -C "ROOT" branch -d factory/<id>`.

The merge is local. Never push. A cleanup failure, often a Windows file lock, does not undo the merge. Report it with the command to rerun.

### Needs-human

When you move a job to `needs-human` yourself, write `jobs/<id>/decision.md` first: `VERDICT: ESCALATE`, a blank line, then the reason. Then update the board, so the dashboard finds the reason. Report the job id and reason to the user, and go on to the next job.

### Decision history

decision.md's first line is always the newest verdict. Whenever you rewrite it, keep the old contents below, under `## Earlier`.

For `/factory approve`, the new head is `VERDICT: APPROVE`, a blank line, then `Approved by a human via /factory approve on <date>.`

### Rework

For `/factory rework <id> <note>` on a needs-human job:

1. Let N be `round + 1`.
2. If spec.md is missing, run **Spec** first, and give the spec-writer the note with the feature text. The Conflicts check applies; a conflict escalates again.
3. Write `<note>` to `round-N/rework.md` under a `# Rework` heading.
4. Run **Build** for round N, then **Review**.

A rework at round 0 follows a spec conflict. Round 1 is then an ordinary round: the builder reads the note, and a CHANGES earns round 2.

A rework after a build does not count toward the two-round cap. It always has N ≥ 2, so any CHANGES sends the job back to needs-human.

## Resume

For `/factory` with no argument, continue each unfinished job from what is on disk, not from memory:

| stage | Do |
|---|---|
| `spec` | Run **Spec**. It skips the spawn when spec.md exists, but still checks Conflicts. |
| `build` | Do not start a new round. If `round-N/build.md` exists, go to Review. Else run Build steps 2 to 4 for round N. |
| `review` | Read each existing review file into the board. Spawn only the reviewers whose file is missing, then wait for all. |
| `approval` | If decision.md was written after the last build.md, act on it. Else spawn approver. |
| `merging` | Run **Merge**. Step 0 skips a merge that already happened. |

On resume, use `builder_agent` from the board to reach the builder.

## Report

When the run ends, list each job you touched with its final stage. For needs-human jobs, give the reason and the two commands: `/factory approve <id>` and `/factory rework <id> <note>`.

If `/factory next` checked off a backlog line, say that `factory/backlog.md` has an uncommitted change.

Mention the dashboard. Run `python -m http.server 8000 --bind 127.0.0.1 -d factory`, then open `http://localhost:8000/dashboard.html`. Binding to localhost keeps job files off the network.
