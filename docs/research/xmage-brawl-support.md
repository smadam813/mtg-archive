# XMage support for Brawl games and the Brawl card pool

Research for XMage support for Brawl games and the Brawl card pool, under the map Arena Brawl support: Standard Brawl, Brawl, and Competitive Brawl. Data date: 2026-10-03. XMage source: magefree/mage at tag `xmage_1.4.61V1`, commit `105d560ece2939d03fe6d052d3479a91c04ca4b2`, dated 2026-08-12. Scryfall: `oracle_cards` of 2026-10-03T21:01Z and `default_cards` of 2026-10-03T21:05Z.

## Answer

- **XMage 1.4.61 has a Brawl duel, but it differs from its Commander duel in two match settings only.** `BrawlDuelMatch` starts players at 25 life and turns commander damage off. The game classes differ only in the minimum deck size (60 against 100), and the game uses that number only for companion legality.
- **The shim can run a Brawl game with parameters.** Pass 25 life and call `setCheckCommanderDamage(false)` on the existing `GoldfishDuel`. A change of base class to `BrawlDuel` adds nothing that a shim game uses. Neither design touches the patch series.
- **XMage implements 93.6% of the Brawl pool and 93.1% of the Standard Brawl pool.** For cards first printed on or before the tag date, the shares are 95.3% and 98.6%. For Brawl cards with a paper printing, the share is 97.2% (99.2% before the tag date).
- **Digital-only cards are the gap: XMage has 52 of 656 (7.9%).** XMage has seek, conjure and draft from a spellbook. It has no perpetual, specialize, boon or intensity. 245 of the 656 cards say "perpetually".
- **There are no `A-` cards to measure.** Scryfall holds no Alchemy rebalanced card on 2026-10-03, and XMage registers none.
- **The Brawl deck validator does not matter for simulation.** It checks a 60-card Standard-set Brawl deck with an old ban list. The shim never calls a validator.

## Brawl game rules in XMage

`BrawlDuel` and `CommanderDuel` are both thin subclasses of `GameCommanderImpl` in the core module. The match classes set the start life and the commander-damage rule. The shim does not use the match classes: `GameRunner` builds a `GoldfishDuel` directly.

| Rule | `BrawlDuel` and `BrawlDuelMatch` | `CommanderDuel` and `CommanderDuelMatch` | Shim `GoldfishDuel` (`GameRunner`) |
|---|---|---|---|
| Base class | `GameCommanderImpl` | `GameCommanderImpl` | `CommanderDuel` |
| Start life | 25 (`BrawlDuelMatch.java:18`) | 40 (`CommanderDuelMatch.java:18`); 20, 30 or 25 for the Duel, MTGO 1v1 and Centurion deck types | 40 (`GameRunner.START_LIFE`) |
| Commander damage | Off: `game.setCheckCommanderDamage(false)` (`BrawlDuelMatch.java:26`) | On, except for the Duel Commander deck type | On (the `GameCommanderImpl` default) |
| Mulligan | The match option. "Game Default" gives a London mulligan. Free mulligans come from the match option, default 0. | Same | `new LondonMulligan(free_mulligans)`; the service sends 1 |
| Turn-1 draw of the starting player | Skipped: `startingPlayerSkipsDraw = true` in `GameCommanderImpl` | Skipped (same field) | Drawn in a game (`turnOneDraw = true`); skipped in a scenario |
| Minimum deck size | 60 (`BrawlDuel.java:13`) | 100 (`CommanderDuel.java:13`) | 100 (from `CommanderDuel`) |
| Opening hand | 7 (match default) | 7 | 7 (`GameRunner.HAND_SIZE`) |
| Command zone and tax | `GameCommanderImpl.initCommanderEffects`: the owner may move the commander to the command zone from the graveyard, exile, hand or library. Each earlier cast from the command zone adds {2}. | Same | Same |
| Game type name | "Brawl Two Player Duel" | "Commander Two Player Duel" | "Commander Two Player Duel" |

Facts behind the table:

- **Minimum deck size.** `GameImpl` stores the number and passes it only to `CompanionAbility.isLegal` (`GameImpl.java:1285`). Only Yorion reads it: "deck.size() >= minimumDeckSize + 20" (`YorionSkyNomad.java:81`). No game code checks the deck size.
- **Commander damage.** `CommanderInfoWatcher` records combat damage only when the flag is on (`CommanderInfoWatcher.java:40`). The state-based action in `GameCommanderImpl.checkStateBasedActions` makes a player lose at 21 or more.
- **Turn-1 draw.** `GameCommanderImpl.init` adds a turn modifier that skips the starting player's draw step when `startingPlayerSkipsDraw` is true (`GameCommanderImpl.java:134-136`). `BrawlDuel` does not change the field. CR 103.8a gives the same rule for a two-player game (reported by the coordinator; the shim's `GoldfishDuel` Javadoc cites it too).
- **Commander types.** `GameCommanderImpl.init` makes every sideboard card a commander, with no type check. It skips a companion only when the sideboard holds more than one card. So a planeswalker, Vehicle or Spacecraft commander already plays in a shim game.
- **No other Brawl code.** A case-insensitive search for "brawl" in the core `Mage` module finds only card names and data files. The only `instanceof` checks are for `GameCommanderImpl`, which both games pass.
- **`BrawlFreeForAll`.** It has the same minimum deck size (60), 30 start life, commander damage off, and a turn-1 draw for the starting player (`BrawlFreeForAll.java:30`).

The 25 life and the absence of commander damage match the Arena facts on the map. The map marks those Arena facts unverified. Arena's turn-1 draw rule is not checked here.

## How the shim can run a Brawl game

**Add parameters to the existing game.** A change of base class gives only the minimum deck size and the game type name. No shim game has a companion, and the type name appears only in logs. In both designs the shim must set the life and the commander-damage flag itself, because those values live in the match class that the shim bypasses.

In a shim game, commander damage does not change a result today. The idle opponent cannot lose (`IdlePlayer.canLose` returns `hasLeft()`), and the pilot declares no attackers. The start life does change results: the pilot pays life for costs, and "life" is a `game_over` reason.

### Option 1: parameters (no build change)

- `engine/xmage/protocol/pilot-wire.schema.json` and its examples: `start_game` gains a format field, or `start_life` and `commander_damage`. Today it has `free_mulligans` but no life or game type.
- `engine/xmage/shim/.../Messages.java`: the `StartGame` record gains the field.
- `engine/xmage/shim/.../GameRunner.java`: the start life comes from the message, not `START_LIFE`. For Brawl, call `game.setCheckCommanderDamage(false)`. The setter is public on `GameCommanderImpl`.
- `engine/xmage/shim/.../GoldfishDuel.java`: optional. A constructor flag can set commander damage. The base class stays `CommanderDuel`.
- `engine/xmage/shim/.../ScenarioRunner.java`: only if a card-correctness fixture needs Brawl life. It uses `GameRunner.START_LIFE` (line 61).
- `src/MtgArchive/Simulation/Xmage/XmageWire.cs` (`XmageRequests.StartGame`) and `XmageEngine.cs`. `FreeMulligans` is one constant (1) for every run, and the simplification list says "one opponent at 40 life".
- Shim tests that build a game or check the schema, for example `GameRunnerTest` and `SchemaConformanceTest`.

The Dockerfile, `pom.xml` and the patch series do not change.

### Option 2: a Brawl base class

- `engine/xmage/Dockerfile`: add `/Mage.Server.Plugins/Mage.Game.BrawlDuel/` to the sparse checkout (line 22) and `Mage.Server.Plugins/Mage.Game.BrawlDuel` to the `mvn -pl` list (line 34). Today only the Commander game types are in both.
- `engine/xmage/shim/pom.xml`: add `org.mage:mage-game-brawlduel:1.4.61`.
- A new class, for example `GoldfishBrawlDuel extends BrawlDuel`, with the same `turnOneDraw` constructor and `copy()`. Java has single inheritance, so one class cannot serve both formats.
- All the wire changes of option 1, because `BrawlDuel` does not set 25 life or commander damage off.

A third design gives one class for both formats without a new module: `GoldfishDuel extends GameCommanderImpl` and takes the minimum deck size as a parameter. It must then implement `getGameType()` and `getNumPlayers()`.

### Effect on the patch series and the gates

- **Patches.** Patch 0001 (determinism) and patch 0002 (game copies) change `GameImpl`, `PlayerImpl`, `MatchImpl`, `Table` and other files. Neither patch touches `GameCommanderImpl`, `CommanderDuel`, `BrawlDuel` or their match classes. Every design above needs no patch change.
- **Version and gates.** `Version.java` hashes the shim's `pom.xml` and `src/main`. Any shim change gives a new engine version. CLAUDE.md requires a parity-gate re-run on a shim change.

## Card coverage

### Method

- **XMage names.** The rule of [XMage coverage of real Commander deck slots](xmage-coverage.md). XMage loads a card only through a set class: `CardScanner` reads each set's `getSetCardInfo()` list. The script reads every active `SetCardInfo` line in the 584 set classes. It skips comments and removes the 57 names in the `unfinished` lists of `SecretsOfStrixhaven.java`, `SecretsOfStrixhavenCommander.java` and `RealityFracture.java`. The registration list is more reliable than the class list. A class name drops punctuation, and a class can exist with no registration, for example `PathOfTheAnimist`.
- **Scryfall pools.** `oracle_cards` gives one object per Oracle ID. A card is in a pool when `legalities.brawl`, `legalities.standardbrawl` or `legalities.competitivebrawl` is `legal`. Banned cards are counted apart. The six meld result faces are left out, because they play only through their front cards.
- **Digital-only.** A card is digital-only when no printing in `default_cards` has `paper` in `games`. A card with any paper printing counts as paper, including a Mystery Booster 2 playtest printing.
- **Name match.** A card matches by its full name or its front-face name, ignoring case and accents. XMage registers split cards by the full name and other multi-face cards by the front-face name. A second pass ignores spaces and punctuation. It matched one card: Scryfall "With Great Power . . ." is XMage "With Great Power...".
- **Groups.** "First Arena set" is the earliest printing with `arena` in `games`. "First printed" is the earliest printing of any kind, compared with the tag date 2026-08-12.
- **Script.** `xmage-brawl-support/brawl_coverage.py` prints every number below and writes `xmage-brawl-support/missing-cards.csv` (1,029 rows). The bulk files are not in the repo.

### Results

| Pool | Cards | XMage has | Share |
|---|---|---|---|
| **Brawl, legal** | 16,004 | 14,975 | **93.6%** |
| With a paper printing | 15,348 | 14,923 | 97.2% |
| Digital-only | 656 | 52 | 7.9% |
| `A-` rebalanced | 0 | 0 | none to measure |
| First printed on or before 2026-08-12 | 15,527 | 14,803 | 95.3% |
| Same, with a paper printing | 14,871 | 14,751 | 99.2% |
| First printed after 2026-08-12 | 477 | 172 | 36.1% |
| Brawl, banned | 37 | 37 | 100% |
| **Standard Brawl, legal** | 5,179 | 4,820 | **93.1%** |
| First printed on or before 2026-08-12 | 4,726 | 4,660 | 98.6% |
| First printed after 2026-08-12 | 453 | 160 | 35.3% |
| Standard Brawl, banned | 0 | 0 | none |
| **Competitive Brawl, legal** | 16,031 | 15,003 | **93.6%** |
| Competitive Brawl, banned | 10 | 9 | 90% |

Standard Brawl holds no digital-only card. Competitive Brawl has the Brawl pool with a different ban list: 10 bans against 37. The one Competitive Brawl ban that XMage lacks is the digital-only card Tajic, Legion's Valor.

### Missing cards by set

The table groups the 1,029 missing Brawl cards by first Arena set. "Of" counts the Brawl-legal cards with that first Arena set.

| Set | Released | Missing | Kind |
|---|---|---|---|
| Reality Fracture (FRA) | 2026-10-02, after the tag | 256 of 268 | paper |
| Reality Fracture Commander (FRC) | 2026-10-02, after the tag | 12 of 12 | paper |
| The Hobbit (HOB) | 2026-08-14, after the tag | 37 of 187 | paper |
| Marvel Super Heroes Commander (MSC) | 2026-06-26 | 45 of 284 | paper |
| Marvel Super Heroes (MSH) | 2026-06-26 | 22 of 270 | paper |
| Secrets of Strixhaven (SOS) | 2026-04-24 | 42 of 255 | paper; 41 are in the `unfinished` list, mostly the new "prepare" layout |
| Alchemy Horizons: Baldur's Gate (HBG) | 2022-07-07 | 82 of 192 | 81 digital, 1 paper |
| Alchemy: Innistrad (YMID) | 2021-12-09 | 48 of 63 | 46 digital, 2 paper |
| Alchemy: Kamigawa (YNEO) | 2022-03-17 | 24 of 30 | digital |
| Alchemy: Dominaria (YDMU) | 2022-10-05 | 25 of 30 | digital |
| Alchemy: Secrets of Strixhaven (YSOS) | 2026-05-19 | 26 of 30 | digital |
| 13 other Alchemy sets, YSNC to YECL | 2022-06-02 to 2026-02-03 | 386 of 390 | 381 digital, 5 paper |
| Jumpstart: Historic Horizons (J21) | 2021-08-26 | 21 of 367 | digital |
| Single cards in BLB, TLA, ECC | 2024-08-02 to 2026-01-23 | 3 | paper |

XMage has set classes for four Alchemy sets only: YMID (15 registrations), YNEO (6), YDMU (14) and YSOS (4). It has no class for HBG or the other 13 Alchemy sets.

Standard Brawl misses 359 cards: FRA 256, SOS 42, HOB 37, MSH 22, and two single cards. The singles are Heirloom Epic, whose registration is commented out (`Bloomburrow.java:161`), and Secret of Bloodbending.

The 120 missing paper cards first printed before the tag date are SOS (42), MSC (45), MSH (22) and 11 single cards. Seven of the singles are Alchemy cards with a later paper printing: Sigardian Evangel, Tenacious Pup, Overcooked, Euru, Acorn Scrounger, and the Jet, Pearl and Sapphire Collectors. Alaundo the Seer has a commented-out registration (`CommanderLegendsBattleForBaldursGate.java:39`).

### `A-` rebalanced cards

- **Scryfall holds no rebalanced card.** `oracle_cards` and `default_cards` hold one name that starts with "A-": A-Town, a playtest card that is legal in no Brawl format. The search `is:rebalanced` returns no card. `/cards/named?exact=` returns 404 for A-Luminarch Aspirant, A-Heartfire Hero, A-Blood Artist and A-Alrund's Epiphany.
- **The reason, from a sibling ticket:** on 2026-09-22 Arena reverted over 200 rebalanced cards to their originals in all formats (reported by the coordinator, not checked here). This disagrees with the map's charting fact that `oracle_cards` keeps `A-` cards.
- **XMage registers no rebalanced card.** Two commented-out lines name one: "A-Heartfire Hero" (`Bloomburrow.java:25`) and "A-Blood Artist" (`Jumpstart.java:24`). The classes `AHeartfireHero` and `ABloodArtist` do not exist at the tag. The one active "A-" name is "A-Wing", a Star Wars custom card.
- **Effect:** if an `A-` name comes back, XMage has no card for it. A lookup that strips "A-" would play the original card, which has different rules text.

### Digital-only mechanics

| Mechanic | XMage support | Evidence | Digital-only Brawl cards with it | XMage has |
|---|---|---|---|---|
| Seek | Yes | `SeekCardEffect`; `PlayerImpl.seekCard` (`PlayerImpl.java:3041`) | 107 | 11 |
| Conjure | Yes | `ConjureCardEffect` | 165 | 14 |
| Spellbook (draft from a spellbook) | Yes | `DraftFromSpellbookEffect` | 64 | 8 |
| Perpetual | No | No class or text in the core module | 245 | 0 |
| Specialize | No | Same | 19 | 0 |
| Boon | No | Same | 24 | 0 |
| Intensity | No | Same | 20 | 0 |
| None of these | | | 122 | 20 |

A card can have more than one mechanic. The script finds a mechanic in the Oracle text: "seek", "perpetually", "conjure", "spellbook", "specialize", "boon with" and "intensity". 29 card classes use the three supported effects. A card that XMage lacks plays as a blank card in the shim (`BlankCard`, ADR 0011).

## Deck validators

`Mage.Deck.Constructed/src/mage/deck/Brawl.java` is the only Brawl validator. There is no Historic Brawl or Freeform Brawl validator. The server config lists one Brawl deck type, "Variant Magic - Brawl".

What `Brawl.java` checks:

- **Card pool:** Standard-legal sets only (`Standard.makeLegalSets()`). The list depends on the server clock. It holds core sets, expansions and Standard-legal supplements released on or after the third most recent rotation set. Arena sets (`SetType.MAGIC_ARENA`) are never Standard-legal, so Alchemy cards fail.
- **Ban list:** the `Constructed` base list (Conspiracy, dexterity, ante, offensive and acorn cards), less 10 acorn cards that Brawl exempts, plus 7 Brawl bans: Force of Will, Pithing Needle, Subtlety, Temporal Manipulation, Time Warp, Ugin's Labyrinth and Wash Away. All 7 are in Scryfall's Brawl ban list of 37. Scryfall's Standard Brawl ban list is empty. The commander ban list is empty.
- **Commander:** the sideboard holds the commander and at most one companion. The commander is a legendary creature, a planeswalker, or a card with "can be your commander". A Vehicle or Spacecraft without that text fails.
- **Deck size:** exactly 60 cards with the commander (61 with a companion). It does not support a 100-card Brawl deck.
- **Singleton:** one copy of each card. Basic lands and cards such as Relentless Rats are exempt.
- **Color identity:** every card fits the commander's identity. A colorless commander may run any number of one basic land type.
- **Companion:** the companion's deck condition, with 60 as the minimum.

The shim calls no validator. It loads decks with `Deck.load(lists, true, false, cache)`: `ignoreErrors` is true and nothing checks the deck (`DeckLibrary.java:95`, `GameRunner.java:77`). No file under `engine/xmage/shim/src/main` names `DeckValidator` or `mage.deck`. So the validator does not matter for simulation. It is also no legality source: it is Standard-only, its ban list is old, and its pool moves with the server clock.

## Open points

- **Arena rules.** The Arena values (25 life, no commander damage, one free mulligan in Brawl and none in Competitive Brawl) come from the map's charting facts and are unverified. Arena's turn-1 draw rule is not checked. The shim forces a turn-1 draw on purpose. The choice belongs to Brawl game rules in simulation and tier 1.
- **Seek determinism.** `PlayerImpl.seekCard` collects the candidates into a `HashSet<Card>` and picks one by a seeded index. `CardImpl` has no `hashCode`, so the set order can change between JVM runs, and the same seed can seek a different card. This is not tested. It affects the 11 seek cards that XMage has.
- **Shim name lookup.** The coverage match ignores case, accents and, in one case, punctuation. The shim looks up names with `CardRepository.findCards`. It may not find "With Great Power . . ." under the Scryfall spelling. This is not tested.
- **Card correctness.** The coverage counts registrations. It does not re-check stub classes or `TODO` notes. The earlier study found both rare.
- **Map charting fact.** Scryfall no longer holds `A-` cards. Scryfall data for Arena Brawl legality and Arena card names owns that question.

## Sources

XMage at commit `105d560e` (permalinks):

- [BrawlDuel.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server.Plugins/Mage.Game.BrawlDuel/src/mage/game/BrawlDuel.java#L13), [BrawlDuelMatch.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server.Plugins/Mage.Game.BrawlDuel/src/mage/game/BrawlDuelMatch.java#L17-L30), [BrawlDuelType.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server.Plugins/Mage.Game.BrawlDuel/src/mage/game/BrawlDuelType.java)
- [CommanderDuel.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server.Plugins/Mage.Game.CommanderDuel/src/mage/game/CommanderDuel.java#L13), [CommanderDuelMatch.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server.Plugins/Mage.Game.CommanderDuel/src/mage/game/CommanderDuelMatch.java#L17-L42)
- [BrawlFreeForAll.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server.Plugins/Mage.Game.BrawlFreeForAll/src/mage/game/BrawlFreeForAll.java#L28-L32), [BrawlFreeForAllMatch.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server.Plugins/Mage.Game.BrawlFreeForAll/src/mage/game/BrawlFreeForAllMatch.java#L17-L31)
- [GameCommanderImpl.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/game/GameCommanderImpl.java#L94-L161), [GameImpl.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/game/GameImpl.java#L1275-L1291), [CommanderInfoWatcher.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/watchers/common/CommanderInfoWatcher.java#L39-L46), [YorionSkyNomad.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Sets/src/mage/cards/y/YorionSkyNomad.java#L80-L81), [MulliganType.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/game/mulligan/MulliganType.java)
- [Brawl.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server.Plugins/Mage.Deck.Constructed/src/mage/deck/Brawl.java), [Standard.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server.Plugins/Mage.Deck.Constructed/src/mage/deck/Standard.java#L37-L59), [Constructed.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/cards/decks/Constructed.java#L215-L223), [SetType.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/constants/SetType.java#L38-L59), [config.xml](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Server/config/config.xml#L87-L88)
- [SeekCardEffect.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/abilities/effects/common/SeekCardEffect.java), [PlayerImpl.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/players/PlayerImpl.java#L3041-L3054), [ConjureCardEffect.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/abilities/effects/common/ConjureCardEffect.java), [DraftFromSpellbookEffect.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/abilities/effects/common/DraftFromSpellbookEffect.java)
- [Bloomburrow.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Sets/src/mage/sets/Bloomburrow.java#L25), [Jumpstart.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage.Sets/src/mage/sets/Jumpstart.java#L24), [CardScanner.java](https://github.com/magefree/mage/blob/105d560ece2939d03fe6d052d3479a91c04ca4b2/Mage/src/main/java/mage/cards/repository/CardScanner.java#L88-L96)

This repo as of this research:

- `engine/xmage/shim/src/main/java/mtgarchive/xmage/GameRunner.java`, `GoldfishDuel.java`, `ScenarioRunner.java`, `DeckLibrary.java`, `IdlePlayer.java`, `Messages.java`, `Version.java`
- `engine/xmage/Dockerfile`, `engine/xmage/shim/pom.xml`, `engine/xmage/patches/0001-determinism.patch`, `engine/xmage/patches/0002-game-copies.patch`, `engine/xmage/protocol/pilot-wire.schema.json`
- `src/MtgArchive/Simulation/Xmage/XmageEngine.cs`, `XmageWire.cs`; `docs/adr/0011-xmage-simulation-engine.md`

Scryfall: [bulk data](https://api.scryfall.com/bulk-data) (`oracle_cards` and `default_cards`), and the search and `/cards/named` endpoints for the `A-` checks, all on 2026-10-03.
