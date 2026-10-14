# XMage replaces our engine for simulation tiers 2 to 4

Our goldfish engine plays 10.2% of the nonbasic slots in real Commander decks, so the skills cannot simulate the decks users bring. **A patched XMage fork, run as a GPL sidecar and driven by our goal-aware pilot, becomes the engine for tiers 2, 3 and 4.** XMage is the only engine that meets both hard requirements at real coverage. It covers 99.8% of the audit sample's nonbasic slots. With the game-copies patch, 10,000 full-length duels under the pilot take 58.6-62.0 s on this host, and the bar is 120 s. The same seed gives the same game. Tier 4 (full rules engine) is now in scope. This ADR supersedes ADR 0002.

The hard requirements are firm, and an option that fails one is out:

- Seeded determinism: the same seed gives the same result.
- Throughput: 10,000 runs finish in under 2 minutes on this host.
- The goal-aware pilot makes the choices. A number that measures an engine's own AI does not answer a done-query.

## How XMage runs

- **The pilot seam.** The pilot stays in .NET, out of process. It reads one engine-neutral pilot view and ranks the legal actions that the engine lists. XMage pays mana and chooses targets, modes and X. Each engine adapter reports card features only for effects that its engine executes. The XMage adapter takes static features from an Oracle-derived profile in our DB, and the "finds" set from XMage's own search filter. The goal predicate stays in .NET.
- **The fork.** Pin one XMage release tag. Carry two patches as a patch series in this repo, and apply them at image build. The determinism patch (56 files) makes XMage's randomness come from the run seed. The game-copies patch (about 12 engine lines) makes one game copy per `getPlayable` call, lets playable-calculation copies share library cards only, and drops the state save at each priority round. Offer the determinism patch upstream. Move to a new tag only on demand, for example when users need new-set cards. On each move, re-run the determinism check and the throughput check before the new tag serves floors.
- **Floors.** ADR 0003 stands: the goal-aware greedy pilot, floor labels and the result envelope. Every floor also records the engine and its version, because seeds cannot give the same games across engines. The envelope's DSL coverage field becomes engine coverage. It lists the cards that the engine does not implement and the cards that the adapter cannot give the pilot features for, so each floor stays attributable (ADR 0006).

## The transition

- **Our engine is the parity reference, then goes.** It keeps serving `/simulate` until XMage passes the parity gate: a paired run on the done-query decks, with undefined nonlands swapped for vanilla filler, where XMage agrees with ours inside a 95% confidence interval. The done-query benchmarks are then re-baselined on XMage. After that, delete our goldfish engine, its adapter and the Effect DSL. Tier 1 is closed-form math and does not change.
- **Staple authoring stops now.** No new batch sessions start. Errata re-review of existing staples continues until cutover, because the parity reference runs on them. The Oracle-clause checks of the cards that the benchmarks use become XMage card-correctness fixtures, because an XMage card bug would make a floor dishonest. At cutover, the definitions, the authoring queue and the parked list retire.
- **Tier 3 stays as ADR 0005 designed it.** Scheduled disruption with no response window runs on XMage. The first tier-4 question, such as real opponent disruption or win rate in 4-seat pods, is chosen in build planning. Each tier-4 question needs its own throughput budget: 10,000 pods against idle opponents already take 114.0 s.

  **Note (2026-10-11, [ADR 0013](0013-interactive-disruption.md)):** the first tier-4 question is interactive disruption: a scripted duel opponent casts the disruption profile as real cards, and our seat gets a response window. ADR 0013 sets its budget. One interactive request at the mode's cap finishes in under 120 s with at most 1% engine errors. If it does not, the interactive cap is 5,000 pairs.

## Considered options

- **Keep our engine and grow the DSL.** It is fast (17-25 s per 10,000 runs), but about 50% coverage costs 41-98 capability PRs and about 1,000 definitions, and that coverage already needs tier-4 subsystems (targets, combat, triggers, layers).
- **A long-term hybrid**, ours for staple-covered decks and XMage for the rest. Rejected: two engines, two adapters and a parity gate for as long as both live.
- **Forge or phase.rs as a sidecar.** Rejected: 10,000 goldfish duels took 528 s and 538 s, about 4.5 times the bar, before any pilot round trips.
- **Import Forge card scripts into our DSL.** Rejected: 7.3% of nonbasic slots map directly and 84.8% need a missing subsystem, so import does not cut engine work. The translated definitions are also GPL-3.0 material.

## Consequences

- XMage and the Java shim are GPL-3.0. They run in their own container, and our service talks to them only over a process boundary. Our code and data take on no GPL terms.
- The throughput margin is about 2x for duels and thin for pods. A pilot change that adds round trips or an XMage tag move can use up the margin, so both re-run the throughput check.
- The shared-library-card change could leak a playable calculation into the real game. 10,000 identical games found no leak with one deck. The parity gate and the fixtures run the wider deck sample.

**Correction (2026-09-27):** XMage is MIT-licensed, not GPL-3.0. The `magefree/mage` repository carries the MIT license at tag `xmage_1.4.61V1`. The shim and the patch series are MIT too. The sidecar design stands, for process isolation and JVM tuning, but the license is not a reason for it. Read "GPL sidecar" above as "sidecar".
