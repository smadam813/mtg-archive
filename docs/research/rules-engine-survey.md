# Rules engine survey against the hard requirements

Research for Survey other rules engines against the hard requirements · 2026-09-26

**Question:** Which other MTG rules engines could serve as our simulation engine, and how does each compare with Forge? The hard requirements come from the map, Simulation engine: build our own or integrate Forge: seeded determinism, 10,000 runs in under 2 minutes on this host, and the goal-aware pilot makes the choices.

Every claim cites a primary source: the engine's repository, source file, license file, issue, or first-party benchmark note. Links pin the commit that was read. Claims that this survey could not confirm carry the mark **unverified**. The Forge column repeats the charting facts in the map's first comment and adds one check.

## Answer

- **Three engines survive beside Forge: XMage, phase.rs and Argentum.** None of them fails a hard requirement on the evidence. None of them proves the throughput bar either, because no engine publishes a goldfish figure.
- **Put XMage and phase.rs into the throughput and determinism spike beside Forge.** XMage is the mature MIT-licensed Java engine with full Commander rules. phase.rs is the permissive Rust engine with the cleanest seam: a pure reducer, a per-game seeded RNG, and a typed list of pending choices.
- **Keep Argentum in reserve.** Its seeding and its external-controller API are the best built, but its own benchmark puts it about three times over our per-run budget, and its training API cannot start a Commander game.
- **Magarena drops.** It plays two players only, has no Commander rules, stops at 2019 cards, and has had no commit since 2023-04-24.
- **Majik drops.** Its card pool is Modern-legal only and it plays 1v1 only.
- **Wagic drops.** It has no batch mode and no controller API, a pilot needs a C++ fork of a GUI game, and it plays two players only. XMage beats it on every column.
- **Many small engines drop** for a missing license, a toy card pool, no activity, or no rules layer. See [Dropped early](#dropped-early).
- **Watch manabrew.** It is a Rust port of Forge that reads Forge card scripts, but today it plays only selected matchups, and it is AGPL.

## The per-run budget

The throughput bar is 10,000 runs in under 120 s, so the engine must finish 83 runs per second. This host has an Intel i9-13900KF: 24 cores and 32 hardware threads (`nproc` = 32). With perfect parallel scaling, the budget is about **0.29 core-seconds per run on 24 cores**, or 0.38 on 32 threads. A run is a goldfish game of up to 10 turns (the default turn horizon in `GLOSSARY.md`). The budget must also cover process start and card-data load, unless one process serves many runs.

Use this budget to read every throughput figure below. A figure for a full AI-vs-AI game over-states the goldfish cost, because AI search and the opponent's turns dominate it.

## Comparison table

| | Forge | XMage | phase.rs | Argentum | Magarena | Wagic | Majik |
|---|---|---|---|---|---|---|---|
| **Verdict** | Baseline | **Spike** | **Spike** | Reserve | Drop | Drop | Drop |
| **License** | GPL-3.0+ | MIT | MIT or Apache-2.0 | MIT | GPL-3.0 | BSD (code) | Apache-2.0 |
| **Runtime** | Java 17, Maven | Java 8 target, CI on JDK 17, Maven | Rust, native or WASM | Kotlin 2.2, JDK 21, Gradle | Java 8, Groovy, Ant | C++ (JGE) | C#, .NET 10 |
| **Activity** | Very active; 2.0.14 on 2026-08-08 | Very active; 1.4.61V1 on 2026-08-12 | Very active; v0.94.0 on 2026-09-25; repo started 2026-03-10 | Very active; repo started 2026-01-18 | Dead; last commit 2023-04-24, last release 2019-12-29 | Slow; last commit 2026-08-30, last release 2025-05-04 | Active; last commit 2026-09-21 |
| **Headless or batch** | `sim` CLI; text output | No CLI; in-process test harness; `LoadTest` needs a server | `ai-commander` binary with `--seed` and `--games-file` batch | `gym` library and HTTP `gym-server`; `benchmark-random` | `DeckStrCal` and `--headless` | None found (GUI game; scripted test suite) | Library plus server; console is diagnostic only |
| **Commander** | Yes, pods | Yes, duel and free-for-all, up to 10 players | Yes, 4-player pods | Yes in the engine, pods of any size; partners pending | No (`MAX_PLAYERS = 2`) | Random-commander mode, two players | No (1v1, Modern pool) |
| **Card coverage** | 33,983 scripts; claims 99.8% of Commander cards | Claims over 32,000 cards | 31,990 of 35,850 cards (89%); Commander 93% (self-reported) | About 15,960 card files | 13,071 scripts; pool ends near 2019 | Claims over 25,000 (**unverified**) | Modern-legal pool only |
| **Seeding and determinism** | Global static RNG; full determinism **unverified**; one JVM per worker | Global static `Random`; unseeded random UUIDs for object IDs; **not deterministic without patches** | RNG lives in each game state (ChaCha20); replay is exact by design | RNG lives in each game state; seed test proves the same opening | Global static RNG; parallel games unsafe | Global C `rand`; seedable in the test suite | **unverified** |
| **Reported throughput** | About 0.04 AI games/s per thread (third party); goldfish **unverified** | No figure; indirect: 6,908 scripted tests in 163 s on one CI runner | About 1.6 s per 1v1 AI game; goldfish **unverified** | About 2 random-action games/s on 8 threads (1v1, full games) | None published | None published | None published |
| **External controller** | GPL Java `PlayerController` (110 abstract methods) | In-process Java `Player` (about 25 decision methods); MIT | Rust `apply(state, actor, action)` over 138 `WaitingFor` kinds | `reset / step / legalActions` over HTTP or in-process | In-process GPL fork | In-process C++ fork | **unverified** |

## Forge (baseline)

The map's first comment holds the Forge facts at HEAD `95dc682`. This survey adds one check. [`PlayerController`](https://github.com/Card-Forge/forge/blob/95dc682/forge-game/src/main/java/forge/game/player/PlayerController.java) is an abstract class with 110 abstract methods. An external pilot must implement all of them in Java that links `forge-game`, so the pilot shim is GPL-3.0.

## XMage

Source: [magefree/mage](https://github.com/magefree/mage) at `2f9e3f0` (2026-09-26).

- **License.** [LICENSE.txt](https://github.com/magefree/mage/blob/2f9e3f0/LICENSE.txt) is MIT. A pilot shim in Java has no copyleft duty.
- **Runtime.** The [root pom](https://github.com/magefree/mage/blob/2f9e3f0/pom.xml) sets `java.version` and `java.release` to 8. The [CI workflow](https://github.com/magefree/mage/blob/2f9e3f0/.github/workflows/maven.yml) builds on JDK 17. The repo is about 1.0 GB.
- **Activity.** The latest release is `xmage_1.4.61V1` (2026-08-12). The last commit is 2026-09-26. The project started in 2012.
- **Headless or batch.** XMage has no simulation CLI. [`LoadTest`](https://github.com/magefree/mage/blob/2f9e3f0/Mage.Tests/src/test/java/org/mage/test/load/LoadTest.java) runs AI-vs-AI games, but its header says it needs a server "started separately", and it polls the server once per second. The practical path is in-process, as the unit-test harness does it: the test base builds a game and a `TestPlayer` per seat and runs it to the end in one JVM.
- **Commander.** The server ships `Mage.Game.CommanderDuel`, `Mage.Game.CommanderFreeForAll` and the freeform variants ([Mage.Server.Plugins](https://github.com/magefree/mage/tree/2f9e3f0/Mage.Server.Plugins)). The [README](https://github.com/magefree/mage/blob/2f9e3f0/readme.md) claims multiplayer Commander with up to 10 players. The test tree has a `CardTestCommanderDuelBase`, and a code search finds 42 files that reference the 4-player base `CardTestCommander4Players`.
- **Coverage.** The README claims "over 32 000 unique cards". Card behavior is Java code, one class per card. The README gives no Commander coverage percentage.
- **Seeding and determinism.** [`RandomUtil`](https://github.com/magefree/mage/blob/2f9e3f0/Mage/src/main/java/mage/util/RandomUtil.java) is one static `java.util.Random` with `setSeed`, and [`Library.shuffle`](https://github.com/magefree/mage/blob/2f9e3f0/Mage/src/main/java/mage/players/Library.java) uses it. But [`MageObjectImpl`](https://github.com/magefree/mage/blob/2f9e3f0/Mage/src/main/java/mage/MageObjectImpl.java) takes each object ID from unseeded `UUID.randomUUID()`, and game state keeps `HashMap`s keyed by those IDs. Iteration order can differ between two runs with the same seed. The open pull request [#13465](https://github.com/magefree/mage/pull/13465) proposes deterministic ability IDs, which confirms that IDs are random today. The MageZero fork rewrote `RandomUtil` as a `ThreadLocal` "for deterministic, parallel simulations" ([WillWroble/mage RandomUtil](https://github.com/WillWroble/mage/blob/master/Mage/src/main/java/mage/util/RandomUtil.java)). Conclusion: XMage needs a seeded ID source and a per-game RNG before it is deterministic. The size of that patch is **unverified**.
- **Throughput.** No first-party figure exists. Two indirect data points:
  - In the CI job of 2026-09-26, the `Mage Tests` module compiled and ran 6,908 tests in 2 min 43 s, with the default single Surefire fork ([run 36249771244](https://github.com/magefree/mage/actions/runs/36249771244)). That is at most about 24 ms per test, but most tests play a few turns with a handful of scripted cards.
  - [MageZero](https://github.com/WillWroble/MageZero) reports about 250 games per hour on 13 threads with a 300-simulation MCTS budget, and says "XMage is heap intensive". That measures search, not goldfish.
  - Goldfish throughput is **unverified**.
- **External controller.** The engine asks a [`Player`](https://github.com/magefree/mage/blob/2f9e3f0/Mage/src/main/java/mage/players/Player.java) object for every decision: `priority`, `chooseMulligan`, `chooseTarget`, `chooseUse`, `choose`, `playMana`, `announceX`, `chooseMode`, `chooseTriggeredAbility`, `chooseReplacementEffect`, `selectAttackers`, `selectBlockers`, `getAmount` and more. `TestPlayer` and MageZero both wrap `ComputerPlayer` and override these methods. Our pilot can sit behind the same seam in a Java shim. XMage has no bot API over the wire, and [issue #10154](https://github.com/magefree/mage/issues/10154) asking for AI-player docs is still open.

**Verdict: spike.** It passes the pilot requirement through an MIT shim. Determinism needs a patch. Throughput needs a measurement.

## phase.rs

Source: [phase-rs/phase](https://github.com/phase-rs/phase) at `c34336e` (2026-09-26).

- **License.** The repo ships [LICENSE-MIT](https://github.com/phase-rs/phase/blob/c34336e/LICENSE-MIT) and [LICENSE-APACHE](https://github.com/phase-rs/phase/blob/c34336e/LICENSE-APACHE). Card data comes from MTGJSON.
- **Runtime.** Rust. The engine compiles to native code and to WASM. A server image `ghcr.io/phase-rs/phase-server` exists. No .NET binding exists. [klyusba/phase-python](https://github.com/klyusba/phase-python) (MIT, started 2026-09-08) shows that a foreign-language binding is possible.
- **Activity.** Very high: v0.93.0 and v0.94.0 came out on consecutive days (2026-09-24 and 2026-09-25). The repo started on 2026-03-10. The README says the project "went from nothing to where it is now in a matter of weeks", and it invites LLM-written card pull requests.
- **Headless or batch.** [`ai_commander.rs`](https://github.com/phase-rs/phase/blob/c34336e/crates/phase-ai/src/bin/ai_commander.rs) plays a 4-player Commander game with every seat on the built-in AI. It takes `--seed`, and `--games-file` runs many `seed,difficulty` lines in one process. Its header says batch mode amortizes a card-database load of "~1.8s/game measured".
- **Commander.** The engine has [`game/commander.rs`](https://github.com/phase-rs/phase/blob/c34336e/crates/engine/src/game/commander.rs) and many commander integration tests.
- **Coverage.** The coverage badges report 31,990 of 35,850 cards (89%) and 93% of Commander-legal cards ([cards.json](https://data.phase-rs.dev/badges/cards.json), [format-commander.json](https://data.phase-rs.dev/badges/format-commander.json)). A parser reads Oracle text into typed effects, and a card counts as covered when no part parses as `Unimplemented` ([CLAUDE.md](https://github.com/phase-rs/phase/blob/c34336e/CLAUDE.md)). The figures are self-reported. How often a covered card plays correctly is **unverified**.
- **Seeding and determinism.** `GameState` holds its own `rng_seed` and a `ChaCha20Rng` ([game_state.rs](https://github.com/phase-rs/phase/blob/c34336e/crates/engine/src/types/game_state.rs)). The [replay module](https://github.com/phase-rs/phase/blob/c34336e/crates/engine/src/game/replay.rs) states that `apply` "is a pure reducer over a `GameState` seeded from a fixed RNG seed", so a replay "reproduces the original game exactly". The RNG is per game, so parallel games in one process do not share state. The [`ai_commander_batch_equivalence`](https://github.com/phase-rs/phase/blob/c34336e/crates/phase-ai/tests/ai_commander_batch_equivalence.rs) tests require that a batch game's output match the single-game output for the same seed "byte-for-byte" outside elapsed time. These tests are `#[ignore]`d in CI because they need generated card data.
- **Throughput.** The committed [perf baseline](https://github.com/phase-rs/phase/blob/c34336e/crates/phase-ai/baselines/perf-baseline.json) records 24,034 ms for 5 samples of 3 AI-vs-AI scenarios, about 1.6 s per 1v1 game with AI search. Goldfish throughput is **unverified**.
- **Stability risk.** Issue [#7134](https://github.com/phase-rs/phase/issues/7134) reports that 38 of 2,000 headless 4-player AI games (1.9%) panicked at one pin, at 7 distinct assertion sites. The issue is closed. Issue [#6967](https://github.com/phase-rs/phase/issues/6967) reports that the AI and perf gates produced 0 passes in 58 CI runs from 2026-07-31. The issue is closed. Fast churn means a pinned version and our own regression suite.
- **External controller.** The public entry point is [`apply(state, actor, action)`](https://github.com/phase-rs/phase/blob/c34336e/crates/engine/src/game/engine.rs). The pending choice is a `WaitingFor` value with 138 variants. The AI lives in a separate crate (`phase-ai`), so a driver that owns the loop can replace it. Our pilot would answer those 138 decision kinds, or pass on the ones a goldfish run never reaches.

**Verdict: spike.** It has the cleanest seam for our pilot and per-game determinism by design. The risks are maturity, churn, and the Rust-to-.NET bridge.

## Argentum

Source: [wingedsheep/argentum-engine](https://github.com/wingedsheep/argentum-engine) at `eb696b0` (2026-09-26).

- **License.** [LICENSE](https://github.com/wingedsheep/argentum-engine/blob/eb696b0/LICENSE) is MIT, plus a trademark notice. GitHub shows `NOASSERTION` only because of that notice.
- **Runtime.** Kotlin 2.2 on JDK 21, Gradle, Spring Boot for the servers.
- **Activity.** Very high. The repo started on 2026-01-18. A separate project, [Blue42hand/commander-gym](https://github.com/Blue42hand/commander-gym), builds a Commander player on it through the gym.
- **Headless or batch.** The `gym` library wraps the engine in `reset / step / observe / legalActions`, with `stepBatch` across a thread pool and O(1) `fork`. The `gym-server` module serves the same contract over HTTP ([README](https://github.com/wingedsheep/argentum-engine/blob/eb696b0/README.md)).
- **Commander.** [backlog/commander-format.md](https://github.com/wingedsheep/argentum-engine/blob/eb696b0/backlog/commander-format.md) marks multiplayer pods done: the command zone, commander tax, commander damage per pair, and `CommanderPodTest` at four seats. Partners, backgrounds and companions are still open. The gym's [`EnvConfig`](https://github.com/wingedsheep/argentum-engine/blob/eb696b0/gym/src/main/kotlin/com/wingedsheep/gym/service/EnvConfig.kt) has no format or commander field, so a Commander game through the gym is **unverified**.
- **Coverage.** The repo tree holds about 15,960 distinct card-definition files under `mtg-sets`. Cards are Kotlin DSL data, grouped by set.
- **Seeding and determinism.** `GameState.rng` holds the RNG, and "two games seeded identically and fed the same actions therefore reproduce byte-identically" ([GameState.kt](https://github.com/wingedsheep/argentum-engine/blob/eb696b0/rules-engine/src/main/kotlin/com/wingedsheep/engine/state/GameState.kt)). [EnvSeedTest](https://github.com/wingedsheep/argentum-engine/blob/eb696b0/gym/src/test/kotlin/com/wingedsheep/gym/service/EnvSeedTest.kt) proves that one seed gives the same opening state.
- **Throughput.** The project's own notes give the best published figures of any engine here:
  - [backlog/engine-performance.md](https://github.com/wingedsheep/argentum-engine/blob/eb696b0/backlog/engine-performance.md): random-action 1v1 sealed games at about 1.9 to 2.0 games per second on 8 threads (Apple M1 Pro).
  - [docs/ai/baseline-metrics.md](https://github.com/wingedsheep/argentum-engine/blob/eb696b0/docs/ai/baseline-metrics.md): 200 random games used 832 s of engine CPU, about 4.2 CPU-seconds per game. Legal-action enumeration takes most of it. AI games run at about 5 games per second on 8 threads, about 11 turns and 497 actions each.
  - Estimate: a 10-turn goldfish run makes fewer decisions than a two-player game, but even one fifth of 4.2 CPU-seconds is about 0.8 CPU-seconds. That is about three times the 0.29 budget. This estimate is **unverified**.
- **External controller.** The gym API is an external controller by design: the caller reads `legalActions` and posts a `step`. Complex choices use a structured response channel.

**Verdict: reserve.** It fails no hard requirement on the evidence, but it is the weakest of the three on throughput, and its gym cannot start a Commander game yet.

## Magarena

Source: [magarena/magarena](https://github.com/magarena/magarena) at `efa0aba` (2023-04-24).

- **License** GPL-3.0 ([LICENSE.txt](https://github.com/magarena/magarena/blob/efa0aba/LICENSE.txt)). **Runtime** Java 8, Groovy and Ant.
- **Activity.** No commit since 2023-04-24. The last release is 1.96 (2019-12-29).
- **Commander.** Not supported. [`DuelConfig.java`](https://github.com/magarena/magarena/blob/efa0aba/src/magic/data/DuelConfig.java) sets `MAX_PLAYERS = 2`.
- **Coverage.** 13,071 card scripts under `release/Magarena/scripts/`. The pool ends near 2019.
- **Seeding.** [`MagicRandom`](https://github.com/magarena/magarena/blob/efa0aba/src/magic/model/MagicRandom.java) is one static generator, seeded by `-DrndSeed`. Parallel games in one JVM are unsafe.
- **Headless.** `DeckStrCal` and `--headless` run AI-vs-AI games. No throughput figure is published.
- **External controller.** Only an in-process Java fork, under the GPL.

**Verdict: drop.** It cannot play Commander, and it is unmaintained.

## Wagic

Source: [WagicProject/wagic](https://github.com/WagicProject/wagic) at `830604d` (2026-08-30).

- **License.** The [LICENSE](https://github.com/WagicProject/wagic/blob/830604d/LICENSE) summary puts the source code under BSD. Resources have other licenses.
- **Runtime.** C++ on the JGE game framework, built for PSP, Android, iOS and desktops.
- **Activity.** 58 commits in 2026 so far. The last release is v0.25.5 (2025-05-04).
- **Commander.** [`Rules.cpp`](https://github.com/WagicProject/wagic/blob/830604d/projects/mtg/src/Rules.cpp) has random-commander game modes, and each player has a command zone. `GameObserver::opponent()` picks the next seat, and no pod mode was found.
- **Coverage.** Release notes claim over 25,000 cards. This survey did not count them, so the figure is **unverified**.
- **Seeding.** `GameApp.cpp` seeds C `rand` from the clock. [`TestSuiteAI.cpp`](https://github.com/WagicProject/wagic/blob/830604d/projects/mtg/src/TestSuiteAI.cpp) reads a `seed` line from a test file. The generator is global.
- **Headless or batch.** None found beyond the scripted test suite.
- **External controller.** Only an in-process C++ fork of the player class.

**Verdict: drop.** No hard requirement fails outright, but a pilot needs a C++ fork of a GUI game with a global RNG, and XMage beats it on every column.

## Majik

Source: [bg9m9r/majik](https://github.com/bg9m9r/majik) at `6f6b2aa` (2026-09-21).

It is the only C# engine found, and it targets .NET 10 like our service. It is Apache-2.0. The [README](https://github.com/bg9m9r/majik/blob/6f6b2aa/README.md) says the engine "ships the Modern-legal card pool" and backs "a free 1v1 client".

**Verdict: drop.** A Modern-only pool fails the coverage bar for Commander decks.

## Dropped early

| Engine | Reason |
|---|---|
| [witchesofthehill/manabrew](https://github.com/witchesofthehill/manabrew) | A Rust port of Forge's engine that reads Forge card scripts, with a seeded parity harness against Java Forge. Its README says the Rust engine "works for selected matchups". Its [LICENSE.md](https://github.com/witchesofthehill/manabrew/blob/3586834/LICENSE.md) is AGPL-3.0-or-later, which is stricter than the GPL sidecar the map accepts. Watch it; do not spike it yet. |
| [lcmaier/MTG-Ichor](https://github.com/lcmaier/MTG-Ichor) | Rust, MIT; targets 4-player Commander, but its state-of-play file lists 175 registered cards |
| [jackmaiorino/mtg-kernel](https://github.com/jackmaiorino/mtg-kernel) | Rust, MIT, deterministic by design; a training kernel over a fixed deck pool |
| [ceverettkoop/robomage](https://github.com/ceverettkoop/robomage) | Runs a fixed set of league decks for reinforcement learning; other cards need a rebuild |
| [elitegoliath/blind_eternities](https://github.com/elitegoliath/blind_eternities) | A rules-question judge, not a game simulator |
| [JensJansen/pauper_sim](https://github.com/JensJansen/pauper_sim) | Curated Pauper subset; no license |
| [Djester808/MtgEngine](https://github.com/Djester808/MtgEngine) | C#, no license |
| [pinky39/grove](https://github.com/pinky39/grove) | C#, GPL-3.0; last commit 2023-03-11 |
| [wanqizhu/mtg-python-engine](https://github.com/wanqizhu/mtg-python-engine) | Python; last commit 2020-12-23 |
| [mtghub-ru/mtghub-engine](https://github.com/mtghub-ru/mtghub-engine) | Common Lisp; last commit 2016-05-20 |
| Cockatrice and similar tabletop clients | No rules layer |
| Forge and XMage wrappers ([commander-ai-lab](https://github.com/KoalaTrapLord/commander-ai-lab), [MageZero](https://github.com/WillWroble/MageZero)) | Not separate engines; their facts appear under Forge and XMage |

## What the spike must measure

The survey found no goldfish throughput figure for any engine. The spike must measure it directly on this host.

1. Drive each engine with a scripted pilot that makes goldfish choices only: keep, land drop, cast by priority, and pass.
2. Run the same decklist and goal for 10,000 runs at the default turn horizon, with one warm process per worker.
3. Report wall time, runs per second, and the per-run core-seconds against the 0.29 budget.
4. Run the same seed twice and compare the full action logs. For XMage, test once as shipped and once with a seeded ID source and a per-game RNG.

## Unverified

- Goldfish throughput for every engine, Forge included.
- Full determinism for Forge, and the patch size that makes XMage deterministic.
- How often phase.rs plays a card that its coverage badge counts as covered.
- Whether the Argentum gym can start a Commander game without engine changes.
- Wagic's card count, and whether Wagic has any multiplayer mode.
- Seeding and the controller seam in Majik.
