---
paths:
  - "src/MtgArchive/Simulation/**/*.cs"
---

# Simulation

- A run has one goal. The options, the play policy, and the mulligan policy must name the same one or none; `MonteCarlo.GoalOf` (in `Engine/`) throws on two. Pilots and mulligan policies are stateless, and they read only the `PilotView`.
- A batch derives each run's seed from the base seed and the run index (`RunSeeds.Derive`). Never draw run seeds from one shared stream; (base seed, run index) must replay one run.
- When a batch aggregates a per-run value, write the invariant that links it to the run verdict on the type that owns the value, and test it. Example: every success has a `GoalProgress.MetTurn`, so the curve at the horizon is at least the success rate; `RunRecord` throws on a success without one.
- Build what does not change between runs once, outside the run loop: `IGameEngine.PrepareAsync` builds the prepared deck once per batch (`XmageDeck` holds the deck's card feature profiles and its sidecar deck id).
- Runs play in parallel, up to `IGameEngine.Parallelism`. Aggregate a per-run value by run index, never by completion order.
- Code under `Simulation/Engine/` names no type of `Simulation/Xmage/`: the pilot, the goal and the batch runner read the pilot view only.
- State the service keeps across a wire exchange with the sidecar (a pending card, the last reply in a window) must be set or cleared by a message the sidecar sends on every exit path of that exchange, failures included. Name that message in the comment on the state. Example: after a failed response the shim sends one more `respond` decide, even with no action left, so `XmageGame._answeredWith` never keeps a card that failed.
