# Scryfall data for Arena Brawl legality and Arena card names

**Question**: What does Scryfall's data say about Arena Brawl legality and Arena card names, and how well does it track Arena?

**Date researched:** 2026-10-03. Counts come from the Scryfall bulk files `oracle-cards-20261003210155.jsonl.gz` and `default-cards-20261003210542.jsonl.gz`, not from the live database. The compose stack was not running. The count script skips the `art_series` and `front_card` layouts, as ingest does, so the counts match what ingest writes to the `card` table.

## Answer

- **One key per Arena queue.** `legalities.brawl` is the Brawl queue (100 cards, formerly Historic Brawl). `legalities.competitivebrawl` is the Competitive Brawl queue. `legalities.standardbrawl` is the Standard Brawl queue. The `competitivebrawl` key is new: Scryfall added it between 2026-06-18 and 2026-06-30, around the 2026-06-23 launch.
- **The ban lists match Wizards.** The 10 `competitivebrawl` bans equal the Wizards list. The 37 `brawl` bans are the 35 on the Wizards page, plus Eye of Ugin (banned 2026-09-22 in the Arena patch notes) and Disruptor Flute. `standardbrawl` has no bans.
- **Latency is about one day.** Scryfall merged the rebalanced cards one day after Arena reverted them. For the 2025 Standard rotation, Scryfall changed Standard a day *before* Arena did. Scryfall rebuilds bulk files every 12 to 24 hours. Exact latency for a single Brawl ban is not measured.
- **There are no `A-` cards any more.** On 2026-09-22 Arena reverted all rebalanced cards. On 2026-09-23 Scryfall merged all 221 rebalanced printings into their originals. `oracle_cards` now holds zero rebalanced cards. The one name that starts with `A-` is a paper playtest card, `A-Town`.
- **Commander eligibility is ours to derive.** No card field marks a Brawl commander. Scryfall has only a search filter, `is:brawler`. We need a rule: legendary creature, legendary planeswalker, legendary Vehicle or Spacecraft with power and toughness, or "can be your commander", and never a meld result.
- **`oracle_cards` holds the Arena pool in its legality keys, not in `games`.** `games`, `arena_id`, and `digital` describe one printing only. The `brawl` status (legal or banned) marks the collectible Arena pool. To find digital-only cards or Arena-only names, read Default Cards.
- **Arena names resolve by oracle name or face name.** Scryfall exact lookup accepts the oracle name, a face name, and `///`. It does not accept a printed name (Through the Omenpaths, Universes Within) or an `A-` name. A printed name resolves only by set and collector number, by fuzzy lookup, or by `!"name"` search.

## 1. Which queue each key describes

Every card object carries all three keys, with a status of `legal`, `not_legal`, `banned`, or `restricted` ([Card objects](https://scryfall.com/docs/api/cards)). The Scryfall search syntax lists `standardbrawl`, `brawl`, and `competitivebrawl` as supported formats ([Search syntax](https://scryfall.com/docs/syntax)). Card pages show `Brawl` and `Comp. Brawl` but not Standard Brawl. Only the API carries `standardbrawl`.

**Competitive Brawl.** Wizards describes it as a ranked format with "most of the same rules as Brawl" and a different ban list. It has 100 cards, a legendary creature, planeswalker, Vehicle, or Spacecraft as commander, 25 life, and no free mulligan. It launched on 2026-06-23 ([Introducing Competitive Brawl](https://magic.wizards.com/en/news/mtg-arena/introducing-ranked-brawl)). Its 10 bans are Ajani, Nacatl Pariah; Nadu, Winged Wisdom; Lutri, the Spellchaser; Oko, Thief of Crowns; Old Stickfingers; Ragavan, Nimble Pilferer; Rusko, Clockmaker; Tamiyo, Inquisitive Student; Wrenn and Six; Tajic, Legion's Valor. Scryfall `banned:competitivebrawl` returns exactly these 10.

**Brawl.** The [Wizards B&R page](https://magic.wizards.com/en/banned-restricted-list) lists 35 Brawl bans. Scryfall has 37. The two extra cards:

- Eye of Ugin. The [2026.63.0 patch notes](https://mtgarena-support.wizards.com/hc/en-us/articles/53820137910932-Patch-Notes-2026-63-0) ban it in Brawl from 2026-09-22. The Wizards B&R page does not list it yet, so Scryfall is ahead of that page.
- Disruptor Flute. Secondary sources say Arena bans it in Brawl. I did not find the Wizards announcement.

**The two 100-card keys share one pool.** `brawl`, `competitivebrawl`, and `historic` each have exactly 20,124 `not_legal` cards. They differ only in their bans. Brawl bans 34 cards that Competitive Brawl allows. Competitive Brawl bans 7 cards that Brawl allows: Ajani, Old Stickfingers, Ragavan, Rusko, Tajic, Tamiyo, and Wrenn and Six. Nadu, Lutri, and Oko are banned under both keys.

**Standard Brawl.** `standardbrawl` has no `banned` status. The 13 cards banned in Standard are `legal` in `standardbrawl`. Arcane Signet and Command Tower are `legal` in `standardbrawl` and `not_legal` in Standard. Wizards publishes no Standard Brawl ban list on its B&R page. Arena made Wastes legal in Standard Brawl on 2025-07-08 ([2025.49.50](https://mtgarena-support.wizards.com/hc/en-us/articles/39138904836116-Patch-Notes-2025-49-50)); Scryfall has Wastes `legal` today.

**Bans as commander only.** Since 2026-08-17 Arena can ban a card "as Commander" while the card stays legal in the deck ([2026.62.0](https://mtgarena-support.wizards.com/hc/en-us/articles/52601349064852-Patch-Notes-2026-62-0)). A Scryfall key has one status per card, so it cannot show this. Today every Brawl and Competitive Brawl ban on the Wizards page says the card "cannot be included in your deck or used as your commander", so no commander-only ban exists yet. When one appears, Scryfall will have to pick one status. The corpus already models a commander-only scope on a ban event (GLOSSARY.md, Ban event).

## 2. Update latency

Scryfall collects bulk data "once every 12-24 hours" ([Bulk data](https://scryfall.com/docs/api/bulk-data)). I measured latency with Wayback Machine snapshots of Scryfall card pages and with the Scryfall [migrations API](https://scryfall.com/docs/api/migrations).

| Arena event | Arena date | Scryfall evidence | Latency |
|---|---|---|---|
| Standard rotation with Edge of Eternities ([2025.49.50](https://mtgarena-support.wizards.com/hc/en-us/articles/39138904836116-Patch-Notes-2025-49-50), [2025.50.00](https://mtgarena-support.wizards.com/hc/en-us/articles/39839452277524-Patch-Notes-2025-50-00)) | 2025-07-29 | Merciless Repurposing (MOM): Standard `legal` at 2025-07-27 10:09 UTC, `not_legal` at 2025-07-28 20:12 UTC | About 1 day *before* Arena |
| Competitive Brawl launch with 10 bans | 2026-06-23 | No `Comp. Brawl` row on Ajani (06-16), Tamiyo (06-17), Ragavan (06-18). `Comp. Brawl` banned on Tamiyo (06-30) and Ajani (07-01) | 7 days or less; exact day unverified |
| Rebalanced cards reverted ([2026.63.0](https://mtgarena-support.wizards.com/hc/en-us/articles/53820137910932-Patch-Notes-2026-63-0)) | 2026-09-22 | 221 `merge` migrations, `performed_at` 2026-09-23, note "Rebalanced cards have been removed from Arena" | 1 day |
| Nadu banned in Brawl (announced 2026-09-11 in [State of the Formats 2026](https://magic.wizards.com/en/news/mtg-arena/state-of-the-formats-2026)) | 2026-09-22 | Original Nadu: Brawl `not_legal` on 2026-09-02, `banned` on 2026-09-28 | 6 days or less; exact day unverified |

The rotation sample checks the `standard` key, because card pages do not show `standardbrawl`. I assume `standardbrawl` rotates at the same time. That is unverified. I found no Standard rotation in 2026; the next one is on 2027-02-02 (State of the Formats 2026).

**Scryfall adds and removes keys.** `competitivebrawl` appeared in June 2026. Scryfall will "remove support for the Alchemy format" on 2027-02-02, and card pages "will cease to list an Alchemy format legality" ([Scryfall thread, 2026-09-13](https://bsky.app/profile/scryfall.com/post/3mvg6lfi3cs2l), [follow-up, 2026-09-21](https://bsky.app/profile/scryfall.com/post/3mvzthnudps2v)). Code that reads a legality key must accept a missing key, both in older snapshots and after a key is dropped.

## 3. Rebalanced (`A-`) cards

**Before 2026-09-22.** Scryfall kept each rebalanced card as a separate oracle card named `A-<name>`, with its own legalities. The Arena legality sat on the `A-` card, and the original was `not_legal` in Arena-only formats. Wayback snapshots of the Nadu pages show this:

| Key | A-Nadu, Winged Wisdom (2026-09-13) | Nadu, Winged Wisdom (2026-09-02) | Nadu, Winged Wisdom (2026-10-03) |
|---|---|---|---|
| Historic | legal | not legal | banned |
| Brawl | legal | not legal | banned |
| Comp. Brawl | banned | not legal | banned |
| Timeless | not legal | legal | legal |
| Commander | not legal | banned | banned |

So Wizards' Competitive Brawl ban on "Nadu, Winged Wisdom" applied, on Scryfall, to the `A-` card only.

**After 2026-09-22.** Arena reverted "over 200 rebalanced versions of tabletop Magic cards" to their original printings "across all formats" ([2026.63.0](https://mtgarena-support.wizards.com/hc/en-us/articles/53820137910932-Patch-Notes-2026-63-0)). Scryfall announced it would "remove all of our database entries for Rebalanced cards" and keep an archive zip ([Scryfall thread](https://bsky.app/profile/scryfall.com/post/3mvg6lfi3cs2l)). The migrations API shows 221 `merge` records on 2026-09-23, each from an `A-` printing to its original printing.

Today:

- `oracle_cards` holds no rebalanced card. Default Cards holds none either.
- `/cards/named?exact=A-Nadu, Winged Wisdom` returns 404. So do A-Symmetry Sage and `/cards/mh3/A-193`. `fuzzy=A-Nadu, Winged Wisdom` returns the original Nadu.
- The only oracle name that starts with `A-` is `A-Town`, a paper playtest card (`set_type` funny). A rule that strips `A-` must try the full name first. `SourceProfileDerivation.SelfName` already keeps the full name.
- Digital-only Alchemy cards stay. Wizards says they "will remain on MTG Arena and legal in Historic, Timeless, Brawl, and Competitive Brawl" (State of the Formats 2026). In `oracle_cards`, 607 cards from `alchemy` sets are `legal` in `brawl`, and 606 of those are also legal in `competitivebrawl` (Tajic, Legion's Valor is banned there). The other 104 are `not_legal`: they are Alchemy Horizons: Baldur's Gate (`hbg`) specialize faces.

An old Arena export can still hold an `A-` name. Arena itself no longer shows one.

## 4. Brawl commander eligibility

No card object field marks Brawl commander eligibility. The card object has no such field ([Card objects](https://scryfall.com/docs/api/cards)). Scryfall search has `is:brawler`, "cards that can be your Brawl Commander" ([Search syntax](https://scryfall.com/docs/syntax)). A search filter is not in the bulk files.

The Arena rule: a legendary creature or planeswalker; since 2025-07-29, "All Legendary spacecraft and vehicles with defined power and toughness" ([2025.50.00](https://mtgarena-support.wizards.com/hc/en-us/articles/39839452277524-Patch-Notes-2025-50-00)). Arena had blocked Vehicles as commanders in February 2025 ([2025.45.10](https://mtgarena-support.wizards.com/hc/en-us/articles/34922780675476-Patch-Notes-2025-45-10)).

I wrote that rule in `count_brawl.py` (front face type line; a meld result is never a commander) and compared it with all 2,411 `is:brawler` results:

| Commander kind | `brawl` legal | `competitivebrawl` legal |
|---|---|---|
| Legendary creature | 2,160 | 2,155 |
| Legendary planeswalker | 236 | 236 |
| Legendary Vehicle with power and toughness | 16 | 16 |
| Legendary Spacecraft with power and toughness | 6 | 6 |
| Says "can be your commander" (and none of the above) | 0 | 0 |
| **Total** | **2,418** | **2,413** |

- `is:brawler` returns 2,411 cards. All 22 of its noncreature, nonplaneswalker results are the 16 Vehicles and 6 Spacecraft above.
- `is:brawler` equals my `brawl` set minus the 7 cards banned only in Competitive Brawl. So `is:brawler` requires a card to be legal in both Brawl and Competitive Brawl.
- Six meld results (Brisela, Hanweir, Mishra, Lost to Phyrexia, Ragnarok, Titania, Urza, Planeswalker) are oracle cards with a legendary type line. They are back faces and cannot be commanders. The current rule in `CardDerivations.CanBeCommander` does not exclude them.
- 258 Brawl commanders are not creatures. Only 3 of them say they can be your commander (Jace, Multiverse Architect; Minsc & Boo, Timeless Heroes; Quintorius, History Chaser). So the Commander rule rejects 255 Brawl commanders.

## 5. Arena availability: `oracle_cards` or Default Cards

`oracle_cards` has "one Scryfall card object for each Oracle ID", and the chosen printing is "the most up-to-date recognizable version" ([Bulk data](https://scryfall.com/docs/api/bulk-data)). `games` ("A list of games that this card print is available in") and `arena_id` belong to that one printing ([Card objects](https://scryfall.com/docs/api/cards)). `digital` is "True if this card was only released in a video game", also per printing.

| Measure | `oracle_cards` printing | All printings (Default Cards) |
|---|---|---|
| Has an Arena printing (`games` has `arena`) | 12,426 | 16,284 |
| `arena_id` set | 11,681 | not counted |
| Digital only | 1,916 (`digital` true) | 854 (every printing digital) |
| `games` is only `arena` | 766 | not counted |

- The `oracle_cards` printing disagrees with "any Arena printing" for 3,858 cards. So `games` and `arena_id` on `oracle_cards` miss about a quarter of the Arena pool.
- `digital` over-counts: 1,062 cards have a digital oracle printing (MTGO Masters Editions, Tempest Remastered, Vintage Masters) and a paper printing too.
- The `brawl` status is the better signal. No card is `legal` or `banned` in `brawl` without an Arena printing. 237 cards have an Arena printing but are `not_legal`. They are 104 `hbg` specialize faces, 75 Star Trek cards (release 2026-11-13), 38 tokens and emblems, and 20 cards whose only Arena printings are in Alchemy (`y…`) or Jumpstart sets, such as Mox Jet.
- Scryfall's `game:arena` search gives 16,284, the same as Default Cards.

So legality questions need only `oracle_cards`. Default Cards is needed for two things: a correct digital-only flag, and printed-name aliases (section 6). Default Cards is 78.7 MB compressed, against 24.6 MB for `oracle_cards` (live `/bulk-data` catalog).

## 6. Arena decklist export to `oracle_id`

**Export format.** Wizards' own help page shows only `<qty> <name>` lines ([Importing a Deck](https://mtgarena-support.wizards.com/hc/en-us/articles/360049857771-Importing-a-Deck)). The community MTG Arena wiki documents the export format ([Deck Import, Wayback 2026-04-11](https://web.archive.org/web/20260411201906/https://magicarena.fandom.com/wiki/Deck_Import)). This is a secondary source:

- A card line is `<amount> <Card Name> (<Set>) <Collector Number>`, for example `4 Aurelia, Exemplar of Justice (GRN) 153`.
- "Split Cards are represented by an entry with both names separated by //", for example `2 Discovery // Dispersal (GRN) 223`.
- "Brawl deck format adds two keywords to the definition 'Commander' and 'Deck'":

```
Commander
1 Alela, Artful Provocateur (ELD) 324

Deck
1 Temple of Silence (M20) 256
```

- An `About` header followed by `Name <deck name>` names the deck.

**Live Scryfall tests (2026-10-03).**

| Input | Endpoint | Result |
|---|---|---|
| `Iron Spider, Stark Upgrade` | `named?exact=` | 200, oracle_id `e123fd7d-…` |
| `Fizik, Etherium Mechanic` (its Through the Omenpaths name) | `named?exact=` | 404 |
| `Fizik, Etherium Mechanic` | `named?fuzzy=` | 200, `om1` #166, same oracle_id `e123fd7d-…` |
| `Fizik, Etherium Mechanic` | `search?q=!"…"` | 1 card, same oracle_id |
| `Fizik, Etherium Mechanic` | `/cards/collection` by name | not_found |
| `om1` / `166` | `/cards/om1/166` and collection by set and number | 200, same oracle_id |
| `The Terminus of Return` (Omenpaths name of The Soul Stone) | `named?exact=` | 404 |
| `Will the Wise` (Universes Beyond name) | `named?exact=` | 404; `fuzzy` gives oracle name `Wernog, Rider's Chaplain` |
| `A-Nadu, Winged Wisdom` | `named?exact=`, collection | 404, not_found |
| `Discovery`, `Discovery // Dispersal`, `Discovery /// Dispersal` | `named?exact=` | all 200, one oracle_id |
| `Bonecrusher Giant`, `Fable of the Mirror-Breaker`, `Shatterskull Smashing` (front faces) | `named?exact=` | 200, the full multiface card |
| `DAR` / `86` (Arena code for Dominaria) | `/cards/dar/86` | 404; `/cards/dom/86` gives Demonlord Belzenlok |
| Arena ID `97984` | `/cards/arena/97984` | 200, Iron Spider, Stark Upgrade |

**How Scryfall stores renamed cards.** Scryfall records each Through the Omenpaths card "as a reprint of the corresponding Spider-Man card", with the same oracle_id. The Omenpaths name sits in `printed_name`, the type in `printed_type_line`, and the text in `printed_text` ([Scryfall blog](https://scryfall.com/blog/through-the-omenpaths-added-plus-english-printed-text-support-235)). For Universes Within cards, the oracle name is the Universes Within name, and the Universes Beyond printing carries its name in `printed_name`. Default Cards has 275 English printings with a `printed_name` that differs from `name` (213 oracle_ids): 153 in `om1`, 93 in `slz`, 29 in `sld`. The `oracle_cards` row for these cards is a Spider-Man or Universes Within printing without `printed_name`. So the corpus `raw` column cannot resolve an Omenpaths name.

**Arena changed again in June 2026.** Arena added Marvel's Spider-Man on 2026-06-23. Omenpaths cards became card styles, and each player got the Spider-Man equivalent ([2026.60.0](https://mtgarena-support.wizards.com/hc/en-us/articles/50325847169300-Patch-Notes-2026-60-0)). Scryfall now gives the `om1` printings `games` = `["mtgo"]` and puts `arena_id` 97984 on the `spm` printing. Arena import already maps names: since 2025-09-16, "importing a deck with card names from a Universes Beyond set will auto-swap to their Through the Omenpaths equivalent" ([2025.52.00](https://mtgarena-support.wizards.com/hc/en-us/articles/41292678660244-Patch-Notes-2025-52-00)).

**Set codes.** Scryfall's set object has `arena_code`. 24 sets have an `arena_code` that differs from `code`, for example `dar` for `dom` and `ms4` for `med` (live `/sets`). The 18 Alchemy (`y…`) sets have no `arena_code`.

**A resolution order that works with today's data:**

1. Drop the `(SET) 123` suffix. Match the name against oracle names, then face names. The corpus resolver (`CardNameResolver` with the face-name fallback) already does this for decklist rows.
2. If no match, try the name with a leading `A-` removed. All rebalanced cards now map to their originals.
3. If no match, look up a printed-name alias. That needs Default Cards or a small alias table.
4. As a last step, map the set code through `arena_code` and look up the collector number. That needs printing data, which the corpus does not keep (GLOSSARY.md, Printing).

`DecklistParser` today takes the whole rest of the line as the name. So `1 Sol Ring (CMR) 263` parses with the name `Sol Ring (CMR) 263`, which then fails to resolve. `Commander`, `Deck`, and `About` lines have no quantity, so they fail the parse.

## 7. Counts

From the 2026-10-03 bulk files; 36,171 oracle cards after the ingest layout filter. `count_brawl.py` and its output `counts.txt` are in `docs/research/scryfall-brawl-legality/`.

| Key | legal | not_legal | banned |
|---|---|---|---|
| `brawl` | 16,010 | 20,124 | 37 |
| `competitivebrawl` | 16,037 | 20,124 | 10 |
| `standardbrawl` | 5,179 | 30,992 | 0 |
| `standard` (for comparison) | 5,164 | 30,994 | 13 |
| `historic` (for comparison) | 15,963 | 20,124 | 84 |
| `commander` (for comparison) | 32,116 | 3,972 | 83 |

No card has `restricted` under any of these keys.

| Other count | Value |
|---|---|
| `brawl` and `competitivebrawl` statuses differ | 41 cards (34 banned only in Brawl, 7 banned only in Competitive Brawl) |
| Rebalanced `A-` cards | 0 (221 printings merged on 2026-09-23) |
| Names that start with `A-` | 1 (`A-Town`, paper playtest) |
| Digital only, every printing | 854 |
| `digital` true on the `oracle_cards` printing | 1,916 |
| Any Arena printing | 16,284 |
| `brawl`-legal Brawl commanders | 2,418 (258 are not creatures) |
| `is:brawler` (Scryfall search) | 2,411 |

## 8. Open and unverified points

- **Exact latency for one Brawl ban.** Wayback gives only bounds: 7 days or less for Competitive Brawl, 6 days or less for Nadu. The migrations API suggests 1 day.
- **Standard Brawl rotation.** Card pages do not show `standardbrawl`, so I could not see it change. I assume it moves with `standard`.
- **Disruptor Flute.** Scryfall bans it in Brawl. The Wizards B&R page does not list it. I did not find the announcement.
- **Standard bans in Standard Brawl.** Scryfall treats Standard-banned cards as legal in Standard Brawl. I found no Wizards statement either way.
- **Arena export details.** The `Commander` and `Deck` headers come from a community wiki, not Wizards. Not checked: a `Companion` or `Sideboard` header in a Brawl export; whether Arena exports double-faced and adventure cards by front name or full name; what name and set code Arena exports for an Omenpaths card style after June 2026.
- **Commander-only bans.** Arena supports them since 2026-08-17. None exists yet. How Scryfall will show one is unknown.

## Sources

All fetched 2026-10-03 unless a date is given.

- Scryfall card objects: https://scryfall.com/docs/api/cards
- Scryfall bulk data: https://scryfall.com/docs/api/bulk-data and the live catalog https://api.scryfall.com/bulk-data
- Scryfall search syntax: https://scryfall.com/docs/syntax
- Scryfall migrations: https://scryfall.com/docs/api/migrations and https://api.scryfall.com/migrations
- Scryfall sets: https://api.scryfall.com/sets
- Scryfall live card lookups: `/cards/named`, `/cards/search`, `/cards/collection`, `/cards/:set/:number`, `/cards/arena/:id` on https://api.scryfall.com
- Scryfall blog, Through the Omenpaths and printed text (2025-09-12): https://scryfall.com/blog/through-the-omenpaths-added-plus-english-printed-text-support-235
- Scryfall on Bluesky, Alchemy sunset thread (2026-09-13): https://bsky.app/profile/scryfall.com/post/3mvg6lfi3cs2l
- Scryfall on Bluesky, Alchemy follow-up (2026-09-21): https://bsky.app/profile/scryfall.com/post/3mvzt7ce5ws2v
- Wizards, Introducing Competitive Brawl (updated 2026-06-08): https://magic.wizards.com/en/news/mtg-arena/introducing-ranked-brawl
- Wizards, MTG Arena State of the Formats 2026 (2026-09-11): https://magic.wizards.com/en/news/mtg-arena/state-of-the-formats-2026
- Wizards, Banned and Restricted list: https://magic.wizards.com/en/banned-restricted-list
- MTG Arena patch notes (Zendesk help center, read through its public API): 2025.45.10 (articles/34922780675476), 2025.49.50 (39138904836116), 2025.50.00 (39839452277524), 2025.52.00 (41292678660244), 2026.60.0 (50325847169300), 2026.62.0 (52601349064852), 2026.63.0 (53820137910932), under https://mtgarena-support.wizards.com/hc/en-us/articles/
- MTG Arena help, Importing a Deck: https://mtgarena-support.wizards.com/hc/en-us/articles/360049857771-Importing-a-Deck
- MTG Arena community wiki, Deck Import (secondary; Wayback 2026-04-11): https://web.archive.org/web/20260411201906/https://magicarena.fandom.com/wiki/Deck_Import
- Wayback Machine snapshots of Scryfall card pages (CDX API), named in section 2 and section 3 with their dates
- Repo context: `src/MtgArchive/Ingest/LegalityRebuildPhase.cs`, `src/MtgArchive/Ingest/ScryfallSnapshotReader.cs`, `src/MtgArchive/Ingest/SourceProfileDerivation.cs`, `src/MtgArchive/CollectionImport/DecklistParser.cs`, `src/MtgArchive/CollectionImport/CardNameResolver.cs`, `docs/research/card-data-source.md`
