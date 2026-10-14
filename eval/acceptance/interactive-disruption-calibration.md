# Interactive disruption calibration: no answers, same floor

Run recorded 2026-10-11. This is a one-time calibration of ADR 0013, not a standing gate. It checks that interactive disruption gives the abstracted floor on a deck that holds no response card. The interactive floor must agree with the abstracted floor inside the 95% Newcombe interval at 10,000 pairs.

**Deck.** `eval/parity/decks/dq2-obeka.json`, the frozen done-query 2 deck. The filler swap replaced every counterspell, and the frozen card feature profiles carry no response feature, so the pilot has no answer.
**Goal.** `on_battlefield` Pithing Needle by turn 4, Commander, horizon 10.
**Profile.** `[{"kind": "counterspell", "turn": 3}]`, in abstracted and in interactive mode. Its tier-3 disrupted floor is above zero (34.0% with the goal-aware mulligan).
**Engine.** Sidecar `xmage-1.4.62V1+5841eec8`, 16 game threads.
**Runs.** `EngineGate parity-baseline` on a scratch cases file with the two cases, 10,000 pairs per comparison, base seed 20260928 and fresh seed 20260929, both mulligan policies. 0 engine errors.

## Result

| Mulligan | Seed | Baseline arm (both modes) | Abstracted disrupted | Interactive disrupted | Interactive minus abstracted, 95% Newcombe |
|---|---|---|---|---|---|
| goal-aware | 20260928 | 5633 | 3404 | 3404 | 0.0 pp [-1.31, +1.31] |
| goal-aware | 20260929 | 5527 | 3343 | 3343 | 0.0 pp [-1.31, +1.31] |
| land-window | 20260928 | 3044 | 1620 | 1620 | 0.0 pp [-1.02, +1.02] |
| land-window | 20260929 | 2901 | 1510 | 1510 | 0.0 pp [-0.99, +0.99] |

Counts are successes of 10,000. **Pass:** each interval contains 0. The two modes give the same count on every row: the real Counterspell counters the same spell that the abstracted event counters, and the deck has nothing to answer it with.

The baseline arms equal the committed `dq2-needle-t4-disrupted` baseline arms (5633 and 5527, 3044 and 2901), so the calibration plays the same games as the standing tier-3 case.

## Control

The same tooling does play the scripted opponent. On the done-query 2 final 99 with its own counterspells (`dq2-final-99`), with the default profile, 1,000 pairs at seed 20260928 and the goal-aware mulligan, the disrupted arm met the goal in 2 runs in abstracted mode and in 60 runs in interactive mode. `POST /simulate` of the same request in interactive mode also gave 60 runs, with an answered rate of 0.101 for the counterspell and 0.058 for the targeted removal.
