# Four formats: Commander and the three Arena Brawl formats

The archive served Commander only, and the vision listed "Formats other than Commander" as a non-goal. The skills could not answer a question about an MTG Arena Brawl deck. **Format becomes a dimension of the corpus, the index, and the simulation engine.** It has four values: `commander`, `brawl`, `competitive_brawl`, and `standard_brawl`. This ADR overturns that non-goal. The model is general. It held these four formats only until Standard joined as a fifth (see the 2026-10-15 note). One collection serves every format. A request that names no format means Commander, so the Commander skills, done-queries, and evals keep working unchanged.

## Format is a dimension of legality

- Legality is per (card, format, role). Commander adds the banlist version, as before. A Brawl format has one current state, with no version axis and no ban events.
- Competitive Brawl is a format of its own, not a play variant of Brawl. It has its own Scryfall key and its own bans, so its legality differs from Brawl's.
- The three Brawl formats share one commander rule. A commander is a legendary creature, a legendary planeswalker, a legendary Vehicle or Spacecraft with power and toughness, or a card that says it can be your commander. A meld result is never a commander. The companion role is open in all three.
- The versions block gains `format` and `legality_as_of`. Card detail returns legality for all four formats.

## Scryfall is the truth for Brawl legality

Ingest reads the Scryfall keys `brawl`, `competitivebrawl`, and `standardbrawl` from `oracle_cards`. The key defines the card pool: `legal` or `banned` is in the pool, and `not_legal` is outside it. Commander stays curated under ADR 0004. ADR 0004 chose curated files because a Commander ban event needs an effective date and an announcement. Brawl legality is current state only, so that reason does not apply.

A Brawl ban cites the card's legality row, which holds the Scryfall URL, the key, and the bulk `updated_at` as-of date. Drift stays a Commander term. The ingest report shows each Brawl format's counts and status changes.

## The service owns the deck verdict

A deck verdict judges one decklist in one format. It covers card legality and deck shape: deck size, copy limit, color identity, commander pairing, and the companion slot. Its status is `legal`, `incomplete`, `illegal`, or `unknown`. These rules are deterministic and structural, like legality, so no model judges them. A new route returns the verdict without simulation.

The verdict replaces the legality verdict on `/simulate`, and an illegal deck still runs (ADR 0003). Deck size is 100 in Commander, Brawl, and Competitive Brawl, and 60 in Standard Brawl.

## The format fixes the game rules

The format selects one game rules record: start life, commander damage, free mulligans, and the turn-1 draw. A request never sets them.

| Format | Start life | Commander damage | Free mulligans | Turn-1 draw |
|---|---|---|---|---|
| Commander | 40 | on | 1 | yes (CR 103.8c) |
| Brawl | 25 | off | 1 | no, on the play (CR 103.8a) |
| Competitive Brawl | 25 | off | 0 | no, on the play |
| Standard Brawl | 25 | off | 1 | no, on the play |

The service sends explicit values to the shim's `GoldfishDuel`. The shim does not know the format. Tier 1 and tier 2 read the same record, so their cards-seen counts stay equal. Brawl formats are duels only. With Commander values, the shim change must give the same success counts on every parity comparison.

## Brawl has no power target

Brackets, the bracket spec, and the Game Changers list stay Commander-only. Wizards publishes no bracket system for Arena, so a scale of our own would have no source of truth. In a Brawl format, the format's bans are the only power limit, and `/bracket-classification` returns a 400. The Brawl cost ladder is the tier-1 screen, then the reach gate, then tier 2.

## Each format has its own popularity order

The format's popularity order replaces `edhrec_rank` everywhere it orders candidates. Brawl and Competitive Brawl use the maintainer's manual Untapped.gg export first, then `edhrec_rank`, then name, then oracle id. Commander and Standard Brawl use `edhrec_rank`. The maintainer exports by hand and commits the CSV to this private repo. No agent calls Untapped's API or scrapes its pages. An answer that a proxy orders names the proxy and its as-of date.

**Note (2026-10-13):** git does not hold the exports. The maintainer keeps them on disk under `data/popularity/`, and git ignores that directory. Compose mounts them read-only at `/data` as before. The old copies stay in the history of `main`. The rule that no agent calls Untapped's API or scrapes its pages does not change.

## Standard joins as a fifth format

**Note (2026-10-15):** Standard, the 60-card constructed format, becomes a fifth format value, `standard`. It is not Standard Brawl. Its first slice serves card legality only.

- Ingest reads the Scryfall key `standard` from `oracle_cards`, as it reads a Brawl key. Commander stays curated under ADR 0004.
- Standard has one current state, with no version axis and no ban events. The versions block carries `legality_as_of`.
- A Standard deck has no commander and no companion seat. Those roles are `not_eligible` for every card.
- Card detail returns legality for all five formats. The ingest report lists Standard after the Brawl formats.
- Standard's popularity order is `edhrec_rank`, as for Standard Brawl.
- The deck routes reject Standard with a 400 for now. The deck verdict, game rules, simulation, and skills follow in later slices.
- Standard's deck shape differs from every Brawl format: a 60-card minimum, a 15-card sideboard, and four copies. The deck verdict slice owns it.

## Considered options

- **Competitive Brawl as a play variant of Brawl.** Rejected: a variant cannot hold a second ban list without a second legality axis.
- **Curated Brawl ban files, as for Commander.** Rejected: Brawl has no dated history to keep. Scryfall matches the Wizards lists with about one day of lag.
- **XMage's `BrawlDuel`.** Rejected: it fixes the rules inside the shim, and it differs from `GoldfishDuel` only in values the service can send.
- **A Brawl power scale of our own, or Commander brackets for Brawl.** Rejected: no source of truth exists to check our own scale. The bracket spec is a four-player tabletop spec.
- **Another Brawl popularity source.** Rejected: no source has a bulk file, an API, or terms that let us store derived ranks.

## Open points and named gaps

- **Standard Brawl deck size and free mulligan.** 60 cards and one free mulligan come from CR 903.12 and secondary sources. No Arena page confirms them.
- **Commander-only bans.** Since 2026-08-17 Arena can ban a card as commander only. None exists yet, and one Scryfall status cannot show one. The first such ban reopens this point.
- **"Choose a Background" in Brawl.** CR pairing rules apply in all formats. No Arena source confirms Backgrounds.
- **Arena printed names.** A name that lives only on Default Cards printings (Omenpaths, Universes Within) does not resolve. A real miss reopens this point.
- **Digital-only ranking.** No retrieval gate ranks the embedded documents of digital-only cards. A real miss reopens this point.

## Consequences

- Brawl answers can lag Arena by about a day, plus the time between bulk rebuilds. The `legality_as_of` date shows the lag.
- The shim change gives a new engine version, so the engine regression gate re-runs.
- The popularity change re-runs the retrieval eval once, because unranked pulled cards move to name order.
- This ADR amends ADR 0003 (deck verdict), ADR 0004 (Commander-only scope), ADR 0006 and ADR 0009 (popularity order). Each carries a note.
