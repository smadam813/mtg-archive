# Engine regression report

Generated 2026-10-11 20:41 UTC by `EngineGate parity` (spec §6.1; see `README.md`). Candidate XMage `xmage-1.4.62V1+49c083b3` against the baseline `xmage mixed: xmage-1.4.62V1+5841eec8, xmage-1.4.62V1+a3d8e53a`, captured 2026-10-11. Each comparison plays 10000 games per engine at base seed 20260928 and horizon 10, with the goal-aware pilot. It passes when the 95% Newcombe interval of the difference (candidate minus baseline) contains 0. A comparison that fails plays again at the fresh seed 20260929 and fails only when it fails there too.

**Verdict: every comparison passes (36 of 36).**

| Comparison | Baseline | Candidate | Difference | 95% interval | Fresh-seed interval | Engine errors | Verdict |
|---|---|---|---|---|---|---|---|
| dq2-needle-t1/goal-aware | 20.2% | 20.2% | +0.0 pp | [-1.1, 1.1] pp | – | 0 | pass |
| dq2-needle-t1/land-window | 7.3% | 7.3% | +0.0 pp | [-0.7, 0.7] pp | – | 0 | pass |
| dq2-demonic-t1/goal-aware | 1.5% | 1.5% | +0.0 pp | [-0.3, 0.3] pp | – | 0 | pass |
| dq2-demonic-t1/land-window | 0.5% | 0.5% | +0.0 pp | [-0.2, 0.2] pp | – | 0 | pass |
| dq2-needle-t2/goal-aware | 33.8% | 33.8% | +0.0 pp | [-1.3, 1.3] pp | – | 0 | pass |
| dq2-needle-t2/land-window | 14.9% | 14.9% | +0.0 pp | [-1.0, 1.0] pp | – | 0 | pass |
| dq2-needle-t3/goal-aware | 50.7% | 50.7% | +0.0 pp | [-1.4, 1.4] pp | – | 0 | pass |
| dq2-needle-t3/land-window | 25.4% | 25.4% | +0.0 pp | [-1.2, 1.2] pp | – | 0 | pass |
| dq2-obeka-t4/goal-aware | 68.9% | 68.9% | +0.0 pp | [-1.3, 1.3] pp | – | 0 | pass |
| dq2-obeka-t4/land-window | 68.8% | 68.8% | +0.0 pp | [-1.3, 1.3] pp | – | 0 | pass |
| dq2-needle-t4-disrupted/goal-aware/baseline | 56.3% | 56.3% | +0.0 pp | [-1.4, 1.4] pp | – | 0 | pass |
| dq2-needle-t4-disrupted/goal-aware/disrupted | 0.0% | 0.0% | +0.0 pp | [-0.0, 0.0] pp | – | 0 | pass |
| dq2-needle-t4-disrupted/land-window/baseline | 30.4% | 30.4% | +0.0 pp | [-1.3, 1.3] pp | – | 0 | pass |
| dq2-needle-t4-disrupted/land-window/disrupted | 0.0% | 0.0% | +0.0 pp | [-0.0, 0.0] pp | – | 0 | pass |
| dq2-final-99-needle-t4-interactive/goal-aware/baseline | 67.6% | 67.6% | +0.0 pp | [-1.3, 1.3] pp | – | 0 | pass |
| dq2-final-99-needle-t4-interactive/goal-aware/disrupted | 6.1% | 6.1% | +0.0 pp | [-0.7, 0.7] pp | – | 0 | pass |
| dq2-final-99-needle-t4-interactive/land-window/baseline | 50.8% | 50.8% | +0.0 pp | [-1.4, 1.4] pp | – | 0 | pass |
| dq2-final-99-needle-t4-interactive/land-window/disrupted | 4.1% | 4.1% | +0.0 pp | [-0.6, 0.6] pp | – | 0 | pass |
| dq1-sol-ring-t2/goal-aware | 32.5% | 32.5% | +0.0 pp | [-1.3, 1.3] pp | – | 0 | pass |
| dq1-sol-ring-t2/land-window | 13.2% | 13.2% | +0.0 pp | [-0.9, 0.9] pp | – | 0 | pass |
| dq1-demonic-t2/goal-aware | 26.9% | 26.9% | +0.0 pp | [-1.2, 1.2] pp | – | 0 | pass |
| dq1-demonic-t2/land-window | 11.4% | 11.4% | +0.0 pp | [-0.9, 0.9] pp | – | 0 | pass |
| bq1-wrenn-and-six-t3-brawl/goal-aware | 77.5% | 77.5% | +0.0 pp | [-1.2, 1.2] pp | – | 0 | pass |
| bq1-wrenn-and-six-t3-brawl/land-window | 77.5% | 77.5% | +0.0 pp | [-1.2, 1.2] pp | – | 0 | pass |
| bq1-wrenn-and-six-t3-competitive/goal-aware | 77.7% | 77.7% | +0.0 pp | [-1.2, 1.2] pp | – | 0 | pass |
| bq1-wrenn-and-six-t3-competitive/land-window | 77.7% | 77.7% | +0.0 pp | [-1.2, 1.2] pp | – | 0 | pass |
| bq1-wrenn-and-six-responses-t3-interactive/goal-aware/baseline | 77.5% | 77.5% | +0.0 pp | [-1.2, 1.2] pp | – | 0 | pass |
| bq1-wrenn-and-six-responses-t3-interactive/goal-aware/disrupted | 3.5% | 3.5% | +0.0 pp | [-0.5, 0.5] pp | – | 0 | pass |
| bq1-wrenn-and-six-responses-t3-interactive/land-window/baseline | 77.5% | 77.5% | +0.0 pp | [-1.2, 1.2] pp | – | 0 | pass |
| bq1-wrenn-and-six-responses-t3-interactive/land-window/disrupted | 3.5% | 3.5% | +0.0 pp | [-0.5, 0.5] pp | – | 0 | pass |
| bq2-jolly-balloon-man-t3/goal-aware | 38.0% | 38.0% | +0.0 pp | [-1.3, 1.3] pp | – | 0 | pass |
| bq2-jolly-balloon-man-t3/land-window | 37.9% | 37.9% | +0.0 pp | [-1.3, 1.3] pp | – | 0 | pass |
| brawl-calibration-t1/goal-aware | 22.0% | 22.0% | +0.0 pp | [-1.1, 1.1] pp | – | 0 | pass |
| brawl-calibration-t1/land-window | 6.9% | 6.9% | +0.0 pp | [-0.7, 0.7] pp | – | 0 | pass |
| brawl-calibration-t1-competitive/goal-aware | 16.8% | 16.8% | +0.0 pp | [-1.0, 1.0] pp | – | 0 | pass |
| brawl-calibration-t1-competitive/land-window | 6.9% | 6.9% | +0.0 pp | [-0.7, 0.7] pp | – | 0 | pass |

## Cases

- `dq2-needle-t1` on `dq2-obeka`: on_battlefield("Pithing Needle", by_turn 1). The done-query 2 goal: tutors to hand, the ritual, the spirit guide and Sol Ring on turn 1.
- `dq2-demonic-t1` on `dq2-obeka`: cast("Demonic Tutor", by_turn 1). The ritual and the spirit guide pay a two-mana spell on turn 1.
- `dq2-needle-t2` on `dq2-obeka`: on_battlefield("Pithing Needle", by_turn 2). Tutors to the top: Vampiric and Mystical Tutor on turn 1, the card drawn on turn 2.
- `dq2-needle-t3` on `dq2-obeka`: on_battlefield("Pithing Needle", by_turn 3). Cantrips: Brainstorm, Ponder, Preordain and Opt dig for the Needle, and the pilot orders and shuffles.
- `dq2-obeka-t4` on `dq2-obeka`: cast("Obeka, Brute Chronologist", by_turn 4). Fetch lands and duals find the three colors of the commander.
- `dq2-needle-t4-disrupted` on `dq2-obeka`: on_battlefield("Pithing Needle", by_turn 4), disruption [counterspell (turn 3), targeted_removal (turn 4)]. Tier 3 under the default disruption profile: both arms.
  The baseline meets the goal in no disrupted run, so this arm checks only that the candidate applies the disruption the same way.
- `dq2-final-99-needle-t4-interactive` on `dq2-final-99`: on_battlefield("Pithing Needle", by_turn 4), disruption [counterspell (turn 3), targeted_removal (turn 4)]. Tier 4, the counter path: the done-query 2 final 99 with its own counterspells and no filler, under the default profile in interactive mode.
- `dq1-sol-ring-t2` on `dq1-golos`: on_battlefield("Sol Ring", by_turn 2). A five-color deck: Demonic Tutor to hand and Vampiric Tutor to the top.
- `dq1-demonic-t2` on `dq1-golos`: cast("Demonic Tutor", by_turn 2). Fetch lands and duals find black in a five-color mana base.
- `bq1-wrenn-and-six-t3-brawl` on `bq1-wrenn-and-six`: cast("Wrenn and Six", by_turn 3). Brawl acceptance query 1 in Brawl: a planeswalker commander from the command zone, on the play at 25 life, one free mulligan.
- `bq1-wrenn-and-six-t3-competitive` on `bq1-wrenn-and-six`: cast("Wrenn and Six", by_turn 3). The same deck in Competitive Brawl: no free mulligan.
- `bq1-wrenn-and-six-responses-t3-interactive` on `bq1-wrenn-and-six-responses`: on_battlefield("Wrenn and Six", by_turn 3), disruption [targeted_removal (turn 3)]. Tier 4, the protection path: the Brawl acceptance query 1 deck with its response features, Wrenn and Six on the battlefield by turn 3 through an Atomize at the end of turn 3, in interactive mode. Shroud, hexproof and protection spells change what the Atomize can target or answer it.
- `bq2-jolly-balloon-man-t3` on `bq2-jolly-balloon-man`: cast("The Jolly Balloon Man", by_turn 3). Brawl acceptance query 2: a 60-card Standard Brawl deck from the collection casts its commander on turn 3.
- `brawl-calibration-t1` on `brawl-calibration`: cast("Ornithopter", by_turn 1). Calibration with a closed form: one 0-mana card, no tutor, no draw. Counts the turn-1 draw (none on the play) and one free mulligan.
- `brawl-calibration-t1-competitive` on `brawl-calibration`: cast("Ornithopter", by_turn 1). The calibration in Competitive Brawl: no free mulligan.

## Decks

### dq2-obeka

Captured from `../acceptance/done-query-2/final-99.json`. 50 cards became filler cards.

- Filler swaps: An Offer You Can't Refuse → Metallic Sliver; Arcane Denial → Alpha Myr; Arcane Signet → Alpha Myr; Baral, Chief of Compliance → Alpha Myr; Chain of Vapor → Metallic Sliver; Chaos Warp → Sliver Construct; Counterspell → Alpha Myr; Cyclonic Rift → Alpha Myr; Dauthi Voidwalker → Alpha Myr; Deadly Dispute → Alpha Myr; Delay → Alpha Myr; Diabolic Intent → Alpha Myr; Dispel → Metallic Sliver; Fabricate → Sliver Construct; Faerie Mastermind → Alpha Myr; Feed the Swarm → Alpha Myr; Fellwar Stone → Alpha Myr; Flusterstorm → Metallic Sliver; Frantic Search → Sliver Construct; Gamble → Metallic Sliver; Gitaxian Probe → Metallic Sliver; Infernal Grasp → Alpha Myr; Ledger Shredder → Alpha Myr; Lightning Bolt → Metallic Sliver; Mana Drain → Alpha Myr; Mind Stone → Alpha Myr; Mindbreak Trap → Gilded Sentinel; Mystic Remora → Metallic Sliver; Narset, Parter of Veils → Sliver Construct; Night's Whisper → Alpha Myr; Notion Thief → Gilded Sentinel; Pact of Negation → Memnite; Phyrexian Arena → Sliver Construct; Pyroblast → Metallic Sliver; Red Elemental Blast → Metallic Sliver; Sensei's Divining Top → Metallic Sliver; Sign in Blood → Alpha Myr; Spell Pierce → Metallic Sliver; Spoils of the Vault → Metallic Sliver; Swan Song → Metallic Sliver; Tainted Pact → Alpha Myr; Talisman of Creativity → Alpha Myr; Talisman of Dominance → Alpha Myr; Talisman of Indulgence → Alpha Myr; Terminate → Alpha Myr; Thassa's Oracle → Alpha Myr; Toxic Deluge → Sliver Construct; Underworld Breach → Alpha Myr; Windfall → Sliver Construct; Wishclaw Talisman → Alpha Myr.
- Lands (38): Arid Mesa, Badlands, Blood Crypt, Bloodstained Mire, Cascade Bluffs, City of Brass, Command Tower, Dragonskull Summit, Drowned Catacomb, Fiery Islet, Flooded Strand, Gloomlake Verge, Graven Cairns, Island, Mana Confluence, Marsh Flats, Misty Rainforest, Mountain, Polluted Delta, Reliquary Tower, Riverpyre Verge, Scalding Tarn, Shivan Reef, Steam Vents, Sulfur Falls, Sunken Hollow, Sunken Ruins, Swamp, Takenuma, Abandoned Mire, Underground River, Underground Sea, Urza's Saga, Vault of Whispers, Verdant Catacombs, Volcanic Island, Watery Grave, Windswept Heath, Wooded Foothills.
- Engine coverage on XMage: unimplemented none; unprofiled Urza's Saga.
- Activation cap (roadmap carried-forward item 3): 0 of 1000 runs reach the cap of two activations per ability per turn.

### dq2-final-99

Captured from `../acceptance/done-query-2/final-99.json`. 0 cards became filler cards.

- Filler swaps: none.
- Lands (38): Arid Mesa, Badlands, Blood Crypt, Bloodstained Mire, Cascade Bluffs, City of Brass, Command Tower, Dragonskull Summit, Drowned Catacomb, Fiery Islet, Flooded Strand, Gloomlake Verge, Graven Cairns, Island, Mana Confluence, Marsh Flats, Misty Rainforest, Mountain, Polluted Delta, Reliquary Tower, Riverpyre Verge, Scalding Tarn, Shivan Reef, Steam Vents, Sulfur Falls, Sunken Hollow, Sunken Ruins, Swamp, Takenuma, Abandoned Mire, Underground River, Underground Sea, Urza's Saga, Vault of Whispers, Verdant Catacombs, Volcanic Island, Watery Grave, Windswept Heath, Wooded Foothills.
- Engine coverage on XMage: unimplemented none; unprofiled Arcane Denial, Baral, Chief of Compliance, Fellwar Stone, Mana Drain, Mystic Remora, Narset, Parter of Veils, Notion Thief.
- Activation cap (roadmap carried-forward item 3): 202 of 1000 runs reach the cap of two activations per ability per turn.

### dq1-golos

Captured from `../acceptance/done-query-1/decklist.json`. 60 cards became filler cards.

- Filler swaps: Ajani Unyielding → Obsianus Golem; Ajani, the Greathearted → Gilded Sentinel; Arcane Signet → Alpha Myr; Arena Rector → Gilded Sentinel; Ashiok, Dream Render → Sliver Construct; Ashiok, Nightmare Muse → Glass Golem; Atraxa, Praetors' Voice → Gilded Sentinel; Birds of Paradise → Metallic Sliver; Chromatic Lantern → Sliver Construct; Commander's Sphere → Sliver Construct; Cyclonic Rift → Alpha Myr; Deepglow Skate → Glass Golem; Deploy the Gatewatch → Obsianus Golem; Doubling Season → Glass Golem; Elspeth, Sun's Champion → Obsianus Golem; Evolution Sage → Sliver Construct; Faeburrow Elder → Sliver Construct; Farseek → Alpha Myr; Fellwar Stone → Alpha Myr; Flux Channeler → Sliver Construct; Garruk, Cursed Huntsman → Obsianus Golem; Gilded Lotus → Glass Golem; Ignite the Beacon → Glass Golem; Inexorable Tide → Glass Golem; Jace, Unraveler of Secrets → Glass Golem; Kiora, the Crashing Wave → Gilded Sentinel; Liliana, Dreadhorde General → Obsianus Golem; Maelstrom Wanderer → Eldrazi Devastator; Merciless Eviction → Obsianus Golem; Narset Transcendent → Gilded Sentinel; Narset, Parter of Veils → Sliver Construct; Nicol Bolas, Dragon-God → Glass Golem; Nicol Bolas, God-Pharaoh → Hexplate Golem; Nicol Bolas, Planeswalker → Eldrazi Devastator; Oath of Ajani → Alpha Myr; Oath of Gideon → Sliver Construct; Oath of Nissa → Metallic Sliver; Oath of Teferi → Glass Golem; Ob Nixilis Reignited → Glass Golem; Path to Exile → Metallic Sliver; Primevals' Glorious Rebirth → Hexplate Golem; Ral Zarek → Gilded Sentinel; Sarkhan the Masterless → Glass Golem; Sensei's Divining Top → Metallic Sliver; Shalai, Voice of Plenty → Gilded Sentinel; Smothering Tithe → Gilded Sentinel; Spark Double → Gilded Sentinel; Supreme Verdict → Gilded Sentinel; Swords to Plowshares → Metallic Sliver; Tamiyo, Collector of Tales → Gilded Sentinel; Tamiyo, Field Researcher → Gilded Sentinel; Tamiyo, the Moon Sage → Glass Golem; Teferi, Hero of Dominaria → Glass Golem; Teferi, Temporal Archmage → Obsianus Golem; Teferi, Time Raveler → Sliver Construct; Tezzeret's Gambit → Gilded Sentinel; The Chain Veil → Gilded Sentinel; Ugin, the Spirit Dragon → Eldrazi Devastator; Urza's Ruinous Blast → Glass Golem; Vraska, Relic Seeker → Obsianus Golem.
- Lands (36): Arid Mesa, Badlands, Bayou, Blood Crypt, Bloodstained Mire, Breeding Pool, City of Brass, Command Tower, Field of the Dead, Flooded Strand, Godless Shrine, Hallowed Fountain, Interplanar Beacon, Mana Confluence, Marsh Flats, Misty Rainforest, Overgrown Tomb, Plateau, Polluted Delta, Reflecting Pool, Sacred Foundry, Savannah, Scalding Tarn, Scrubland, Steam Vents, Stomping Ground, Taiga, Temple Garden, Tropical Island, Tundra, Underground Sea, Verdant Catacombs, Volcanic Island, Watery Grave, Windswept Heath, Wooded Foothills.
- Engine coverage on XMage: unimplemented none; unprofiled Golos, Tireless Pilgrim.
- Activation cap (roadmap carried-forward item 3): 0 of 1000 runs reach the cap of two activations per ability per turn.

### bq1-wrenn-and-six

Captured from `../acceptance/brawl-query-1/deck.txt`. 0 cards became filler cards.

- Filler swaps: none.
- Lands (37): Arid Mesa, Ash Barrens, Bloodstained Mire, Boseiju, Who Endures, Cinder Glade, Command Tower, Demolition Field, Evolving Wilds, Fabled Passage, Forest ×8, Game Trail, Karplusan Forest, Misty Rainforest, Mountain ×7, Prismatic Vista, Rogue's Passage, Rootbound Crag, Stomping Ground, Temple of Abandon, Terramorphic Expanse, Verdant Catacombs, Windswept Heath, Wooded Foothills, Yavimaya, Cradle of Growth.
- Engine coverage on XMage: unimplemented none; unprofiled Boseiju, Who Endures, Cultivate, Demolition Field, Entish Restoration, Jeska's Will, Lotus Cobra, Solemn Simulacrum, Sylvan Library, The One Ring, Utopia Sprawl, Veil of Summer, Wood Elves.
- Activation cap (roadmap carried-forward item 3): 53 of 1000 runs reach the cap of two activations per ability per turn.

### bq1-wrenn-and-six-responses

Captured from `../acceptance/brawl-query-1/deck.txt`. 0 cards became filler cards.

- Filler swaps: none.
- Lands (37): Arid Mesa, Ash Barrens, Bloodstained Mire, Boseiju, Who Endures, Cinder Glade, Command Tower, Demolition Field, Evolving Wilds, Fabled Passage, Forest ×8, Game Trail, Karplusan Forest, Misty Rainforest, Mountain ×7, Prismatic Vista, Rogue's Passage, Rootbound Crag, Stomping Ground, Temple of Abandon, Terramorphic Expanse, Verdant Catacombs, Windswept Heath, Wooded Foothills, Yavimaya, Cradle of Growth.
- Engine coverage on XMage: unimplemented none; unprofiled Boseiju, Who Endures, Cultivate, Demolition Field, Entish Restoration, Jeska's Will, Lotus Cobra, Solemn Simulacrum, Sylvan Library, The One Ring, Utopia Sprawl, Veil of Summer, Wood Elves.
- Activation cap (roadmap carried-forward item 3): 53 of 1000 runs reach the cap of two activations per ability per turn.

### bq2-jolly-balloon-man

Captured from `../acceptance/brawl-query-2/parity-deck.txt`. 0 cards became filler cards.

- Filler swaps: none.
- Lands (24): Boros Guildgate, Command Tower, Elegant Parlor, Evolving Wilds, Fabled Passage, Mountain ×7, Plains ×6, Sacred Foundry, Starting Town, Sundown Pass, Temple of Triumph, Vibrant Cityscape, Wind-Scarred Crag.
- Engine coverage on XMage: unimplemented none; unprofiled Crime Novelist, Heraldic Banner.
- Activation cap (roadmap carried-forward item 3): 0 of 1000 runs reach the cap of two activations per ability per turn.

### brawl-calibration

Captured from `sources/brawl-calibration.txt`. 0 cards became filler cards.

- Filler swaps: none.
- Lands (40): Wastes ×40.
- Engine coverage on XMage: unimplemented none; unprofiled Karn, Legacy Reforged.
- Activation cap (roadmap carried-forward item 3): 0 of 1000 runs reach the cap of two activations per ability per turn.

## Carried-forward checks

- Item 3, the activation cap: see each deck above.
- Item 4, the turn-1 draw: the turn-1 comparisons cover it. XMage's Commander duel skips that draw; the shim restores it (`GoldfishDuel`).
