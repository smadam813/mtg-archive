# XMage coverage of real Commander deck slots

Research for XMage coverage of real Commander deck slots, under the map Simulation engine: build our own or integrate Forge. Data date: 2026-09-26.

## Answer

- **XMage 1.4.61 implements 99.8% of the sample's nonbasic slots:** 4,425 of 4,432. Forge has 100% of the same slots.
- **Nonland slots: 99.8%** (3,255 of 3,262). **Nonbasic lands: 100%** (1,170 of 1,170). **Unique nonbasic cards: 2,440 of 2,447.**
- **7 cards are missing, one slot each.** Six come from sets of 2026 that XMage has not finished. The seventh, Path of the Animist, needs Planechase rules that XMage has not enabled.
- **Both repo decks are fully covered.** The Golos deck and the Obeka deck have 100% of their nonbasic slots in XMage. The EDHREC average decks lose 1 slot of 2,110. The Archidekt decks lose 6 slots of 2,125.
- **10 present cards carry a `TODO` note (16 slots).** Only 3 of these notes name a rules gap, and each gap is a narrow edge case. If these 16 slots also count as gaps, coverage is 99.5% of nonbasic slots and 99.3% of nonland slots.

## Question

What share of real Commander deck slots does XMage implement, measured on the same sample as the Forge column of the coverage audit? A card counts when XMage has a card class for it and does not mark the class unfinished.

## Sample

The sample is the one from Mechanics coverage audit: what real Commander decks need, unchanged: 52 decks, 5,200 slots, 4,432 nonbasic slots, 2,453 unique cards. It is on branch `research/mechanics-coverage-audit`, in `docs/research/mechanics-coverage-audit/sample.json` and `card-coverage.csv`.

This study reads the audit's `card-coverage.csv`. That file gives each card's archive name, its slot count, its basic flag, and its Forge status. A copy is in `xmage-coverage/card-coverage.csv`. The slot counts and the Forge column come from that file without change.

A card is a land when its front-face type line holds "Land". This is the audit's rule. An MDFC such as Agadeem's Awakening is a nonland card, because its front face is a sorcery. `dump_sample_cards.py` takes the type lines from the archive's Scryfall snapshot into `sample-cards.csv`.

## Method

1. **XMage source.** magefree/mage at tag `xmage_1.4.61V1`, commit `105d560ece2939d03fe6d052d3479a91c04ca4b2`, dated 2026-08-12. The license is MIT. A blob-less sparse clone of `Mage.Sets/src/mage` is enough. No build is needed.
2. **Registered cards.** XMage loads a card only through a set class: `CardScanner` fills the card database from each set's `getSetCardInfo()` list. Each set class in `Mage.Sets/src/mage/sets/` (584 files) adds its cards with `cards.add(new SetCardInfo("Name", ..., mage.cards.<letter>.<Class>.class))`. The script reads every active `SetCardInfo` line. It skips lines in `//` and `/* */` comments. The 32,122 card classes are in `Mage.Sets/src/mage/cards/<letter>/`.
3. **Names.** A sample card matches when its full name or its front-face name equals a registered name or the front part of one. This is the rule of the Forge column. XMage registers split cards by the full name ("Fire // Ice"). It registers transform, MDFC, adventure, meld, and prepare cards by the front-face name ("Agadeem's Awakening"). The match ignores case and accents. All 75 present cards with a special layout matched: 20 MDFC, 15 transform, 14 saga, 8 split, 8 adventure, 4 class, 4 meld, and 2 case. Baldin, Century Herdmaster is registered under its own name, so it needs no flavor-name rule.
4. **XMage markers of unfinished cards.** XMage has no committed list of unimplemented cards. `Utils/gen-list-unimplemented-cards-for-set.pl` builds such a list per set into `Utils/data/`, and Git ignores that folder (`Utils/readme.txt`). The markers in the tree are these, in the order the script applies them:
   - **Absent from the set.** A set class lists only the cards that XMage implements. A card that is in no active `SetCardInfo` line is missing.
   - **`unfinished` list.** Three set classes hold a `private static final List<String> unfinished` and remove those names with `cards.removeIf(setCardInfo -> unfinished.contains(setCardInfo.getName()))`. The three are `SecretsOfStrixhaven.java`, `SecretsOfStrixhavenCommander.java`, and `RealityFracture.java`, with 57 names in total. The class exists, but players cannot use the card.
   - **Commented-out registration.** 252 `SetCardInfo` lines are in `//` comments. Such a card has no active registration unless another set registers it.
   - **Stub class.** The script flags a registered class as a stub when its code has no ability or effect call and the card has rules text. 350 of the 32,122 classes have no such call. They are vanilla cards, for example Alpine Grizzly. No sample card is a stub.
   - **Notes in the class.** 104 classes carry a `TODO` note, and 10 carry a "not implemented" note. A note does not remove a card from play, so the script reports these cards as present and lists them apart.
5. **Weights.** A card's weight is its slot count from `card-coverage.csv`. A card in 20 decks weighs 20.

## Results

### Coverage

| Measure | XMage 1.4.61 | XMage, cards with a `TODO` note counted as gaps | Forge |
|---|---|---|---|
| Nonbasic slots (4,432) | 4,425 (99.8%) | 4,409 (99.5%) | 4,432 (100%) |
| Nonland slots (3,262) | 3,255 (99.8%) | 3,239 (99.3%) | 3,262 (100%) |
| Nonbasic land slots (1,170) | 1,170 (100%) | 1,170 (100%) | 1,170 (100%) |
| All slots with basics (5,200) | 5,193 (99.9%) | 5,177 (99.6%) | 5,200 (100%) |
| Unique nonbasic cards (2,447) | 2,440 | 2,430 | 2,447 |

| Source | Nonbasic slots | XMage | Forge |
|---|---|---|---|
| Golos deck (collection, done-query 1) | 100 | 100% | 100% |
| Obeka deck (done-query 2) | 97 | 100% | 100% |
| EDHREC average decks | 2,110 | 2,109 (99.95%) | 100% |
| Archidekt public decks | 2,125 | 2,119 (99.7%) | 100% |

### Missing cards

All 7 are nonland cards, and each fills one slot in one deck.

| Card | Set, release date | XMage marker | Deck |
|---|---|---|---|
| Ashling, the Limitless | Lorwyn Eclipsed Commander (ECC), 2026-01-23 | Absent: `LorwynEclipsedCommander.java` lists 175 cards but not this one | Archidekt 18744843, "Dance of the Elements" |
| Dimension X Pizzasaur | Teenage Mutant Ninja Turtles Eternal (TMC), 2026-03-06 | Absent from `TeenageMutantNinjaTurtlesEternal.java` | Archidekt 20105223, "Turtle Power!" |
| Foot Chopper | Teenage Mutant Ninja Turtles Eternal (TMC), 2026-03-06 | Absent from `TeenageMutantNinjaTurtlesEternal.java` | Archidekt 20105223, "Turtle Power!" |
| Eccentric Pestfinder // Turn Stones | Secrets of Strixhaven Commander (SOC), 2026-04-24 | `unfinished` list in `SecretsOfStrixhavenCommander.java`; the class `EccentricPestfinder.java` exists | Archidekt 21319521, "Witherbloom Pestilence" |
| Stensian Sanguinist // Exsanguinate | Secrets of Strixhaven Commander (SOC), 2026-04-24 | `unfinished` list in `SecretsOfStrixhavenCommander.java`; the class `StensianSanguinist.java` exists | Archidekt 21319521, "Witherbloom Pestilence" |
| Wizard's Staff | The Hobbit (HOB), 2026-08-14 | Absent from `TheHobbit.java`. The set released two days after the tag. | EDHREC average deck, Vivi Ornitier |
| Path of the Animist | March of the Machine Commander (MOC), 2023-04-21 | Commented-out registration in `MarchOfTheMachineCommander.java`, with the other "Path of the ..." cards. The class `PathOfTheAnimist.java` exists and uses `WillOfThePlaneswalkersEffect`. | Archidekt 6780851, "Radha's Explosive Vegetables" |

Six of the 7 come from sets released less than seven months before the tag, or after it. Newer XMage releases may add them. Path of the Animist is an older card: its "Will of the Planeswalkers" text makes players planeswalk or roll for chaos, which needs Planechase rules.

### Present cards with a note

10 present cards carry a `TODO` note. None carries a "not implemented" note. The table groups them by what the note says.

| Kind of note | Cards (slots) | What the note says |
|---|---|---|
| Rules edge case | Doubling Season (3) | The token replacement "should only apply to effects". The class does not check that the tokens come from an effect. |
| | Crawling Sensation (2) | The once-per-turn check "does not work correctly if control changes". |
| | Spark Double (2) | The extra counter is wrong when a type-changing effect such as Blood Moon touches the copied permanent. |
| Code structure, no rules gap named | Strong, the Brutish Thespian (2) | The effect "should be restructured as a replacement", but "it doesn't interfere with anything else yet". |
| | The Great Henge (1), The Skullspore Nexus (1) | The cost reduction uses `abilityToModify` where it "should be source", as a workaround for XMage bug #11166. |
| | Valgavoth, Terror Eater (1) | A refactor note for XMage issue #13594. |
| AI hint or code example | Selvala's Stampede (2), Leyline Tyrant (1) | The computer player needs better hints. Our pilot makes those choices. |
| | Scapeshift (1) | A code example for XMage issue #8254. |

The 3 rules edge cases need an unusual board: a token made by a cost, a control change, or a type-changing effect on a copy target.

## Limits

- The check measures registration, not correctness. It does not measure XMage's rules bugs, as the Forge column did not measure Forge's.
- The stub test is a text test. It finds classes with no ability or effect code. It does not find a class whose code covers only part of the card's text. XMage's own verify tests compare classes with card data, but this study did not run them.
- XMage's `TODO` notes are sparse: 104 of 32,122 classes. A class with no note can still have a gap.
- The tag is dated 2026-08-12. The sample decks were fetched 2026-09-26, so they can hold cards newer than the tag. Wizard's Staff is one.

## What this means for the map

- XMage's card coverage is not the constraint. On this sample it is within 0.2 points of Forge for nonbasic and for nonland slots.
- Both engines cover all of the repo's own decks.
- The gaps are cards from new Commander products. An integration must plan to follow XMage releases, or accept a short lag on new sets.

## Reproduce

The tools and data are in `docs/research/xmage-coverage/`.

1. Make a blob-less sparse clone of XMage at the tag:
   ```
   git clone --filter=blob:none --no-checkout --depth 1 --branch xmage_1.4.61V1 https://github.com/magefree/mage.git mage
   git -C mage sparse-checkout set Mage.Sets/src/mage/sets Mage.Sets/src/mage/cards Utils
   git -C mage checkout
   ```
2. `python dump_sample_cards.py card-coverage.csv` writes `sample-cards.csv`. It needs the `mtg-archive` compose stack's Postgres. The committed `sample-cards.csv` makes this step optional.
3. `python xmage_coverage.py <mage> <sample.json> > analysis.txt` prints every number in this note and writes `xmage-coverage.csv`. `<sample.json>` is the audit's file on branch `research/mechanics-coverage-audit`. It only names the deck of each missing card.

`xmage-coverage.csv` lists every sample card with its slots, layout, land flag, Forge status, XMage status, XMage class, and any note.

## Sources

- magefree/mage at `xmage_1.4.61V1` (`105d560e`): `Mage/src/main/java/mage/cards/repository/CardScanner.java` lines 54-55, `Mage.Sets/src/mage/sets/*.java`, `Mage.Sets/src/mage/cards/`, `Utils/readme.txt`, and `Utils/gen-list-unimplemented-cards-for-set.pl`.
- `Mage.Sets/src/mage/sets/SecretsOfStrixhavenCommander.java` lines 15 and 453: the `unfinished` list and the `removeIf` call.
- `Mage.Sets/src/mage/sets/MarchOfTheMachineCommander.java` line 229: the commented-out Path of the Animist line.
- The card classes named in the tables above, for the `TODO` notes.
- Mechanics coverage audit: what real Commander decks need, branch `research/mechanics-coverage-audit`: the sample, `card-coverage.csv`, and the Forge column.
- Archive database, Scryfall snapshot `updated_at` 2026-09-25: type lines, layouts, and the set and release date of the missing cards.
