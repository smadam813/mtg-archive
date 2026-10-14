# Engine parity report

Generated 2026-09-30 21:07 UTC by `EngineGate parity` (spec §6.1; see `README.md`). XMage `xmage-1.4.61V1+409366e0` against our engine `goldfish 1.0.0+f05e6f80a6585a331e86821437db82d650b78ba3`, whose baseline was captured 2026-09-28. Each comparison plays 10000 games per engine at base seed 20260928 and horizon 10, with the goal-aware pilot. It passes when the 95% Newcombe interval of the difference (XMage minus ours) contains 0. A comparison that fails plays again at the fresh seed 20260929 and fails only when it fails there too.

**Verdict: 11 of 18 comparisons pass, 7 accepted.**

| Comparison | Ours | XMage | Difference | 95% interval | Fresh-seed interval | Engine errors | Verdict |
|---|---|---|---|---|---|---|---|
| dq2-needle-t1/goal-aware | 21.6% | 20.2% | -1.4 pp | [-2.5, -0.3] pp | [-2.9, -0.6] pp | 0; fresh 0 | accepted |
| dq2-needle-t1/land-window | 8.0% | 7.3% | -0.7 pp | [-1.4, 0.1] pp | – | 0 | pass |
| dq2-demonic-t1/goal-aware | 2.0% | 1.5% | -0.4 pp | [-0.8, -0.1] pp | [-0.7, -0.0] pp | 0; fresh 0 | accepted |
| dq2-demonic-t1/land-window | 0.5% | 0.5% | -0.1 pp | [-0.3, 0.1] pp | – | 0 | pass |
| dq2-needle-t2/goal-aware | 34.7% | 33.8% | -1.0 pp | [-2.3, 0.4] pp | – | 0 | pass |
| dq2-needle-t2/land-window | 15.2% | 14.9% | -0.3 pp | [-1.2, 0.7] pp | – | 0 | pass |
| dq2-needle-t3/goal-aware | 51.1% | 50.7% | -0.4 pp | [-1.8, 1.0] pp | – | 0 | pass |
| dq2-needle-t3/land-window | 26.1% | 25.4% | -0.8 pp | [-2.0, 0.4] pp | – | 0 | pass |
| dq2-obeka-t4/goal-aware | 75.2% | 68.9% | -6.4 pp | [-7.6, -5.1] pp | [-6.9, -4.4] pp | 0; fresh 0 | accepted |
| dq2-obeka-t4/land-window | 75.2% | 68.8% | -6.4 pp | [-7.6, -5.1] pp | [-6.8, -4.4] pp | 0; fresh 0 | accepted |
| dq2-needle-t4-disrupted/goal-aware/baseline | 55.2% | 56.3% | +1.1 pp | [-0.3, 2.5] pp | – | 0 | pass |
| dq2-needle-t4-disrupted/goal-aware/disrupted | 0.0% | 0.0% | +0.0 pp | [-0.0, 0.0] pp | – | 0 | pass |
| dq2-needle-t4-disrupted/land-window/baseline | 30.8% | 30.4% | -0.3 pp | [-1.6, 0.9] pp | – | 0 | pass |
| dq2-needle-t4-disrupted/land-window/disrupted | 0.0% | 0.0% | +0.0 pp | [-0.0, 0.0] pp | – | 0 | pass |
| dq1-sol-ring-t2/goal-aware | 29.1% | 32.5% | +3.4 pp | [2.1, 4.6] pp | [1.2, 3.8] pp | 0; fresh 0 | accepted |
| dq1-sol-ring-t2/land-window | 11.5% | 13.2% | +1.7 pp | [0.8, 2.6] pp | [0.2, 2.0] pp | 0; fresh 0 | accepted |
| dq1-demonic-t2/goal-aware | 25.7% | 26.9% | +1.2 pp | [0.0, 2.5] pp | [0.1, 2.6] pp | 0; fresh 0 | accepted |
| dq1-demonic-t2/land-window | 10.4% | 11.4% | +1.0 pp | [0.1, 1.8] pp | [-0.0, 1.7] pp | 0; fresh 0 | pass (fresh seed) |

## Accepted differences

Each comparison below fails on both seeds. The user accepted it for the cause its evidence shows. A defect on the XMage side is fixed, not accepted.

| Comparison | Direction | Verdict | Cause | Evidence |
|---|---|---|---|---|
| dq2-needle-t1/goal-aware | xmage_lower | accepted | On turn 1, XMage enters the checklands and Sunken Hollow tapped. Our engine has no such rule. | diagnosis/small-fails.md |
| dq2-demonic-t1/goal-aware | xmage_lower | accepted | On turn 1, XMage enters the checklands and Sunken Hollow tapped. Our engine has no such rule. | diagnosis/small-fails.md |
| dq1-sol-ring-t2/goal-aware | xmage_higher | accepted | Land order: XMage plays lands in name order, our engine in draw order. The user accepts the name order. | diagnosis/small-fails.md |
| dq1-sol-ring-t2/land-window | xmage_higher | accepted | Land order: XMage plays lands in name order, our engine in draw order. The user accepts the name order. | diagnosis/small-fails.md |
| dq2-obeka-t4/goal-aware | xmage_lower | accepted | After the shim fixes, XMage is within −0.5 pp [−1.8, 0.8] of our engine with the land order and the land rules it lacks (o4), so those rules explain the gap: Sunken Hollow enters tapped, the filter lands and Verges, Fiery Islet's draw, and Urza's Saga. | diagnosis/obeka-t4.md |
| dq2-obeka-t4/land-window | xmage_lower | accepted | After the shim fixes, XMage is within −0.5 pp [−1.8, 0.8] of our engine with the land order and the land rules it lacks (o4), so those rules explain the gap: Sunken Hollow enters tapped, the filter lands and Verges, Fiery Islet's draw, and Urza's Saga. | diagnosis/obeka-t4.md |
| dq1-demonic-t2/goal-aware | xmage_higher | accepted | Land order: XMage plays lands in name order, our engine in draw order. The user accepts the name order. Three land rules our engine lacks (Interplanar Beacon, Reflecting Pool, Field of the Dead) give part of it back. | diagnosis/small-fails.md |

## Cases

- `dq2-needle-t1` on `dq2-obeka`: on_battlefield("Pithing Needle", by_turn 1). The done-query 2 goal: tutors to hand, the ritual, the spirit guide and Sol Ring on turn 1.
- `dq2-demonic-t1` on `dq2-obeka`: cast("Demonic Tutor", by_turn 1). The ritual and the spirit guide pay a two-mana spell on turn 1.
- `dq2-needle-t2` on `dq2-obeka`: on_battlefield("Pithing Needle", by_turn 2). Tutors to the top: Vampiric and Mystical Tutor on turn 1, the card drawn on turn 2.
- `dq2-needle-t3` on `dq2-obeka`: on_battlefield("Pithing Needle", by_turn 3). Cantrips: Brainstorm, Ponder, Preordain and Opt dig for the Needle, and the pilot orders and shuffles.
- `dq2-obeka-t4` on `dq2-obeka`: cast("Obeka, Brute Chronologist", by_turn 4). Fetch lands and duals find the three colors of the commander.
- `dq2-needle-t4-disrupted` on `dq2-obeka`: on_battlefield("Pithing Needle", by_turn 4), disruption [counterspell (turn 3), targeted_removal (turn 4)]. Tier 3 under the default disruption profile: both arms.
  Our engine meets the goal in no disrupted run, so this arm checks only that XMage applies the disruption the same way.
- `dq1-sol-ring-t2` on `dq1-golos`: on_battlefield("Sol Ring", by_turn 2). A five-color deck: Demonic Tutor to hand and Vampiric Tutor to the top.
- `dq1-demonic-t2` on `dq1-golos`: cast("Demonic Tutor", by_turn 2). Fetch lands and duals find black in a five-color mana base.

## Decks

### dq2-obeka

Captured from `../acceptance/done-query-2/final-99.json`. 50 cards became filler cards; 11 staples keep their definitions.

- Filler swaps: An Offer You Can't Refuse → Metallic Sliver; Arcane Denial → Alpha Myr; Arcane Signet → Alpha Myr; Baral, Chief of Compliance → Alpha Myr; Chain of Vapor → Metallic Sliver; Chaos Warp → Sliver Construct; Counterspell → Alpha Myr; Cyclonic Rift → Alpha Myr; Dauthi Voidwalker → Alpha Myr; Deadly Dispute → Alpha Myr; Delay → Alpha Myr; Diabolic Intent → Alpha Myr; Dispel → Metallic Sliver; Fabricate → Sliver Construct; Faerie Mastermind → Alpha Myr; Feed the Swarm → Alpha Myr; Fellwar Stone → Alpha Myr; Flusterstorm → Metallic Sliver; Frantic Search → Sliver Construct; Gamble → Metallic Sliver; Gitaxian Probe → Metallic Sliver; Infernal Grasp → Alpha Myr; Ledger Shredder → Alpha Myr; Lightning Bolt → Metallic Sliver; Mana Drain → Alpha Myr; Mind Stone → Alpha Myr; Mindbreak Trap → Gilded Sentinel; Mystic Remora → Metallic Sliver; Narset, Parter of Veils → Sliver Construct; Night's Whisper → Alpha Myr; Notion Thief → Gilded Sentinel; Pact of Negation → Memnite; Phyrexian Arena → Sliver Construct; Pyroblast → Metallic Sliver; Red Elemental Blast → Metallic Sliver; Sensei's Divining Top → Metallic Sliver; Sign in Blood → Alpha Myr; Spell Pierce → Metallic Sliver; Spoils of the Vault → Metallic Sliver; Swan Song → Metallic Sliver; Tainted Pact → Alpha Myr; Talisman of Creativity → Alpha Myr; Talisman of Dominance → Alpha Myr; Talisman of Indulgence → Alpha Myr; Terminate → Alpha Myr; Thassa's Oracle → Alpha Myr; Toxic Deluge → Sliver Construct; Underworld Breach → Alpha Myr; Windfall → Sliver Construct; Wishclaw Talisman → Alpha Myr.
- Lands (38): Arid Mesa, Badlands, Blood Crypt, Bloodstained Mire, Cascade Bluffs, City of Brass, Command Tower, Dragonskull Summit, Drowned Catacomb, Fiery Islet, Flooded Strand, Gloomlake Verge, Graven Cairns, Island, Mana Confluence, Marsh Flats, Misty Rainforest, Mountain, Polluted Delta, Reliquary Tower, Riverpyre Verge, Scalding Tarn, Shivan Reef, Steam Vents, Sulfur Falls, Sunken Hollow, Sunken Ruins, Swamp, Takenuma, Abandoned Mire, Underground River, Underground Sea, Urza's Saga, Vault of Whispers, Verdant Catacombs, Volcanic Island, Watery Grave, Windswept Heath, Wooded Foothills. XMage runs each land's full text; our engine reads only its produced colors and its fetch activation, so a fail can come from a land (spec §10).
- Engine coverage on XMage: unimplemented none; unprofiled Urza's Saga.
- Activation cap (roadmap carried-forward item 3): 0 of 1000 runs reach the cap of two activations per ability per turn.

### dq1-golos

Captured from `../acceptance/done-query-1/decklist.json`. 60 cards became filler cards; 3 staples keep their definitions.

- Filler swaps: Ajani Unyielding → Obsianus Golem; Ajani, the Greathearted → Gilded Sentinel; Arcane Signet → Alpha Myr; Arena Rector → Gilded Sentinel; Ashiok, Dream Render → Sliver Construct; Ashiok, Nightmare Muse → Glass Golem; Atraxa, Praetors' Voice → Gilded Sentinel; Birds of Paradise → Metallic Sliver; Chromatic Lantern → Sliver Construct; Commander's Sphere → Sliver Construct; Cyclonic Rift → Alpha Myr; Deepglow Skate → Glass Golem; Deploy the Gatewatch → Obsianus Golem; Doubling Season → Glass Golem; Elspeth, Sun's Champion → Obsianus Golem; Evolution Sage → Sliver Construct; Faeburrow Elder → Sliver Construct; Farseek → Alpha Myr; Fellwar Stone → Alpha Myr; Flux Channeler → Sliver Construct; Garruk, Cursed Huntsman → Obsianus Golem; Gilded Lotus → Glass Golem; Ignite the Beacon → Glass Golem; Inexorable Tide → Glass Golem; Jace, Unraveler of Secrets → Glass Golem; Kiora, the Crashing Wave → Gilded Sentinel; Liliana, Dreadhorde General → Obsianus Golem; Maelstrom Wanderer → Eldrazi Devastator; Merciless Eviction → Obsianus Golem; Narset Transcendent → Gilded Sentinel; Narset, Parter of Veils → Sliver Construct; Nicol Bolas, Dragon-God → Glass Golem; Nicol Bolas, God-Pharaoh → Hexplate Golem; Nicol Bolas, Planeswalker → Eldrazi Devastator; Oath of Ajani → Alpha Myr; Oath of Gideon → Sliver Construct; Oath of Nissa → Metallic Sliver; Oath of Teferi → Glass Golem; Ob Nixilis Reignited → Glass Golem; Path to Exile → Metallic Sliver; Primevals' Glorious Rebirth → Hexplate Golem; Ral Zarek → Gilded Sentinel; Sarkhan the Masterless → Glass Golem; Sensei's Divining Top → Metallic Sliver; Shalai, Voice of Plenty → Gilded Sentinel; Smothering Tithe → Gilded Sentinel; Spark Double → Gilded Sentinel; Supreme Verdict → Gilded Sentinel; Swords to Plowshares → Metallic Sliver; Tamiyo, Collector of Tales → Gilded Sentinel; Tamiyo, Field Researcher → Gilded Sentinel; Tamiyo, the Moon Sage → Glass Golem; Teferi, Hero of Dominaria → Glass Golem; Teferi, Temporal Archmage → Obsianus Golem; Teferi, Time Raveler → Sliver Construct; Tezzeret's Gambit → Gilded Sentinel; The Chain Veil → Gilded Sentinel; Ugin, the Spirit Dragon → Eldrazi Devastator; Urza's Ruinous Blast → Glass Golem; Vraska, Relic Seeker → Obsianus Golem.
- Lands (36): Arid Mesa, Badlands, Bayou, Blood Crypt, Bloodstained Mire, Breeding Pool, City of Brass, Command Tower, Field of the Dead, Flooded Strand, Godless Shrine, Hallowed Fountain, Interplanar Beacon, Mana Confluence, Marsh Flats, Misty Rainforest, Overgrown Tomb, Plateau, Polluted Delta, Reflecting Pool, Sacred Foundry, Savannah, Scalding Tarn, Scrubland, Steam Vents, Stomping Ground, Taiga, Temple Garden, Tropical Island, Tundra, Underground Sea, Verdant Catacombs, Volcanic Island, Watery Grave, Windswept Heath, Wooded Foothills. XMage runs each land's full text; our engine reads only its produced colors and its fetch activation, so a fail can come from a land (spec §10).
- Engine coverage on XMage: unimplemented none; unprofiled Golos, Tireless Pilgrim.
- Activation cap (roadmap carried-forward item 3): 0 of 1000 runs reach the cap of two activations per ability per turn.

## Carried-forward checks

- Item 3, the activation cap: see each deck above.
- Item 4, the turn-1 draw: the turn-1 comparisons cover it. XMage's Commander duel skips that draw; the shim restores it (`GoldfishDuel`).
