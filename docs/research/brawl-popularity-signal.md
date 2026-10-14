# Brawl popularity signal to replace EDHREC rank

**Question:** Is there a Brawl play-rate or popularity source that can take the place of EDHREC rank (Scryfall `edhrec_rank`)? Cover Standard Brawl, Brawl, and Competitive Brawl: per-card and per-commander data, the access method, the terms of use, the update frequency, and the stability. Measure how far EDHREC rank and Brawl popularity disagree where a source allows it.

**Researched:** 2026-10-03. Each claim was checked against the cited page on that date, unless it is marked **unverified**.

## Answer

No source gives a Brawl popularity signal that we can fetch automatically and store under its terms. No source has a bulk file or a documented API for Brawl popularity. Scryfall, MTGJSON, and EDHREC carry no Brawl rank.

Two sources have useful per-card Brawl data, but their terms forbid reuse without permission:

- **MTGGoldfish:** per-card "% of decks" and per-commander share for Brawl (100 cards) only. The sample is about 100 user-submitted decks in 30 days.
- **Untapped.gg:** per-card popularity and game counts for Brawl and Competitive Brawl, from its tracker app, with a commander filter.

**AetherHub** is the only source with Standard Brawl data. It gives per-commander shares only, from about 49 decks in 90 days.

EDHREC rank agrees moderately with Brawl play on cards and does not agree on commanders:

- Cards: Spearman ρ = 0.41 against MTGGoldfish's Brawl lists (142 cards).
- Commanders: ρ = 0.04 against AetherHub's Brawl commander shares (45 commanders).

EDHREC rank is null for Arena-only digital cards and for Commander-banned cards. Today these cards sort last, even when Brawl players use them.

These options are for the later decision. They are not a decision:

1. Keep `edhrec_rank` with a Brawl legality filter, and give null ranks a fallback order.
2. Ask Untapped, MTGGoldfish, or AetherHub for written permission to store derived ranks.
3. Aggregate Archidekt Brawl decklists with explicit permission from Archidekt staff.

## Sources compared

| Source | Standard Brawl | Brawl | Competitive Brawl | Granularity | Access | Terms (automated access, stored ranks) | Update | Maturity |
|---|---|---|---|---|---|---|---|---|
| Scryfall | Legality only | Legality only | Legality only | Per card: `edhrec_rank`, `penny_rank`. No Brawl rank | Bulk file, documented API | Free for Magic software | Daily bulk | Mature |
| MTGJSON | No | No | No | Per card: `edhrecRank`, `edhrecSaltiness` | Bulk file | MIT | Daily | Mature |
| EDHREC | No | No | No | No Brawl pages | Undocumented JSON | Forbids automated queries and copying | n/a | n/a for Brawl |
| MTGGoldfish | No | Yes | No | Per card (% of decks, top 50 per list); per commander (share) | HTML only, no API | Personal use only; no reproduction or derivative works; no automated-access clause | Window 7 to 365 days (default 30) | About 100 decks in 30 days, user-submitted |
| AetherHub | Yes ("Brawl") | Yes ("Historic Brawl") | No | Per commander (share, matches); per card inside one commander | HTML only, no API | No automated-access clause; footer reserves "use" and reproduction | Window 7 to 90 days, tracker uploads | About 49 and 310 decks in 90 days |
| Untapped.gg | No | Yes ("Historic Brawl") | Yes | Per card (popularity, games, win rates); commander filter | Undocumented API, HTML | Forbids download and scraping without written permission | Not stated | Tracker data, active product |
| Moxfield | Deck format | Deck format | Deck format | Decklists only | No public API | Forbids robots and automatic access unless approved | n/a | Deck source |
| Archidekt | Deck format | Deck format | Not found | Decklists only | Undocumented API | Forbids automated queries; staff allow throttled use informally | n/a | Deck source |
| 17lands | No | No | No | Limited only | Bulk CSV | CC BY 4.0 | Per set | Mature, Limited only |
| MTGArena.pro | Not found | Not found | Not found | Meta page empty | HTML | Personal temporary download only; no copying | n/a | Degraded |
| mtgdecks.net | Not read | Pages exist (**unverified**) | Not read | Per card "most played" (**unverified**) | HTML | robots.txt blocks AI agents | Not read | Not read |

## Per source

### Scryfall

- The card object has two popularity fields. `edhrec_rank`: "This card's overall rank/popularity on EDHREC. Not all cards are ranked." `penny_rank`: "This card's rank/popularity on Penny Dreadful." There is no Brawl rank ([Card objects](https://scryfall.com/docs/api/cards)).
- The search `order` values include `edhrec` and `penny`. There is no Brawl order ([Search](https://scryfall.com/docs/api/cards/search)).
- The `legalities` object has `standardbrawl`, `brawl`, and `competitivebrawl` keys. A live read of Arcane Signet returned all three as `legal`. Ragavan, Nimble Pilferer is `banned` in `competitivebrawl` and `legal` in `brawl`. This fact is also input for #259.
- `q=legal:brawl -t:basic` returned 15,998 cards on 2026-10-03.
- Terms, bulk files, and rate limits are in `docs/research/staple-playrate-data.md` and did not change for this question.

### MTGJSON

- The card model has `edhrecRank` ("The card rank on EDHRec") and `edhrecSaltiness`. It has no Brawl field ([Card (Atomic)](https://mtgjson.com/data-models/card/card-atomic/)).

### EDHREC

- The site has no Brawl data. The `everything-else` sitemap has no Brawl page. The only "brawl" matches are card names such as "Tavern Brawler" ([sitemap](https://edhrec.com/sitemaps/everything-else.xml)).
- The FAQ says EDHREC drops decks that are not legal Commander decks: "our database will not pick it up intentionally" ([FAQ](https://edhrec.com/faq)). A 60-card or Arena-only Brawl deck fails that check.
- The terms still forbid automated queries and copying ([Terms](https://edhrec.com/terms)).

### MTGGoldfish

- **Format:** one format, "Brawl". It is the 100-card Arena Brawl. All 145 nonbasic cards in its staples lists are `brawl`-legal on Scryfall, and 75 of them are not `standardbrawl`-legal. There is no Standard Brawl or Competitive Brawl format ([metagame](https://www.mtggoldfish.com/metagame/brawl)).
- **Per card:** "Most Played Cards in Brawl", with "% of Decks" and "# Played". The full lists hold 50 rows each: cards overall (nonland), creatures, spells, and lands ([format staples](https://www.mtggoldfish.com/format-staples/brawl), [full list](https://www.mtggoldfish.com/format-staples/brawl/full/all)). The lowest listed share is 9% for nonland cards and 7% for lands.
- **Per commander:** the metagame page shows a share and a deck count per commander. The default window is 30 days, with 7, 14, 90, and 365 as options. The smallest share is 1.9% for 2 decks, so the 30-day sample is about 100 to 105 decks.
- **Data source:** user-submitted decks. The "Recent Brawl Tournaments" page lists user decks with names such as "brawl chandra" ([tournaments](https://www.mtggoldfish.com/tournaments/brawl)).
- **Access:** HTML only. This research found no API and no export for the staples lists. robots.txt does not disallow `/format-staples` or `/metagame` ([robots.txt](https://www.mtggoldfish.com/robots.txt)).
- **Terms:** "The Website and the contents are intended solely for personal, non-commercial use. You may download or copy the contents ... for your personal use only. ... You may not reproduce (except as noted above), publish, transmit, distribute, display, modify, create derivative works from, sell or exploit in any way any of the contents or the Website." The terms have no clause on automated access ([Terms of Use](https://www.mtggoldfish.com/policies/terms-of-use)). A stored rank is a derivative of the contents, so storing it needs consent.

### AetherHub

- **Formats:** the format menu has "Brawl" and "Historic Brawl", and no Competitive Brawl ([Historic Brawl meta](https://aetherhub.com/Metagame/Historic-Brawl/), [Brawl meta](https://aetherhub.com/Metagame/Brawl/)).
- "Brawl" on AetherHub is Standard Brawl. All 8 commanders on that page are `standardbrawl`-legal on Scryfall. "Historic Brawl" is the 100-card Brawl. Many of its commanders, such as Hei Bai, Forest Guardian, are not `standardbrawl`-legal.
- **Per commander:** a share of the metagame, a match count, and a weekly change. Windows are 7, 30, 60, and 90 days.
- **Sample:** an archetype page reads "11 decks, 3.55% of meta", so the 90-day Historic Brawl sample is about 310 decks. The Standard Brawl page's shares are multiples of 1/49, so its 90-day sample is about 49 decks.
- **Per card:** only inside one commander archetype ("Meta deck card breakdown - 11 decks"). There is no format-wide card list ([archetype page](https://aetherhub.com/Metagame/Historic-Brawl/Deck/hei-bai-forest-guardian-1429905)).
- **Data source:** the MTGA Assistant tracker: "Download the MTGA Assistant extension to contribute anonymously with data."
- **Access:** HTML only. The Docs page lists no data API ([Docs](https://aetherhub.com/Docs/)). robots.txt is empty.
- **Terms:** last modified 3 September 2017. They list breaches such as "overloading AetherHub's servers", but have no clause on automated access ([Terms and Conditions](https://aetherhub.com/TermsAndConditions)). The page footer says: "All original content on this page is ©2026 AetherHub.com and may not be used or reproduced without consent."

### Untapped.gg

- **Formats:** the format menu lists "Competitive Brawl" and "Historic Brawl". It has no Standard Brawl. `/constructed/standard-brawl` returns an error page ([Historic Brawl](https://mtga.untapped.gg/constructed/historic-brawl), [Competitive Brawl](https://mtga.untapped.gg/constructed/competitive-brawl)).
- **Per card:** the card data page has "Popularity", "Total Games", and win-rate columns. It has a "Commander & Card" filter, a "MIN GAMES 500+" filter, and an "EXPORT AS CSV" button ([Historic Brawl cards](https://mtga.untapped.gg/constructed/historic-brawl/cards)). Whether the export needs Premium is **unverified**.
- **Access:** the page loads data from `https://api.mtga.untapped.gg`, which has no public documentation. robots.txt allows `*`, and disallows `/decks/*` and `*rank=*` for named search bots ([robots.txt](https://mtga.untapped.gg/robots.txt)).
- **Terms:** "a limited, revocable, non-exclusive, non-transferable, non-sublicensable license to use and access the Service, solely for your personal, non-commercial use." Also: "You agree that you shall not modify, copy, distribute, frame, reproduce, republish, download, scrape, display, post, transmit, or sell in any form or by any means, in whole or in part, or otherwise exploit the Service without our express prior written permission." The last substantive update was 2019-07-19 ([Terms of Service](https://untapped.gg/legal/terms-of-service)).

### Moxfield

- **Formats:** the site's own format table has "Standard Brawl" (exactly 60 cards, legality key `standardbrawl`), "Brawl" (id `historicBrawl`, exactly 100 cards, legality key `brawl`, legacy key `historicbrawl`), and "Competitive Brawl" (exactly 100 cards, legality key `competitivebrawl`). Source: the public JS bundle served to moxfield.com.
- **Data:** decklists only. Moxfield publishes no format-wide popularity numbers.
- **Access:** no public API. `api2.moxfield.com/robots.txt` disallows `/`.
- **Terms** (effective 9 July 2026): users may not "use any robot, spider or other automatic device, process or means to access the Site for any purpose, including monitoring or copying any information or material on the Site, except as expressly approved by Moxfield" ([Terms of Service](https://moxfield.com/help/terms)).

### Archidekt

- **Formats:** third-party client code maps Archidekt deck format 13 to "Brawl" (Standard Brawl, 60) and 20 to "Historic Brawl" (100). One client notes that "the site has swapped these before" ([manabrew](https://github.com/witchesofthehill/manabrew/blob/3ca311c29e552c244c3dd5a9374f95d085323976/src/lib/archidekt.ts), [MtgaCollectionAdvisor](https://github.com/Dasayeve/MtgaCollectionAdvisor/blob/1641db9f91e950b38e18de40f9fe534e167ac4ac/src/MtgaCollectionAdvisor.Core/Decks/ArchidektClient.cs)). **Unverified** on Archidekt itself.
- **Data:** decklists only. No popularity numbers.
- **Terms:** users may not "use software or automated agents or scripts ... to generate automated searches, requests, or queries to the Site" ([Terms](https://archidekt.com/terms)). A staff forum post welcomes throttled API use with a link back. `docs/research/staple-playrate-data.md` cites that post; it was not re-checked here.

### 17lands

- Public datasets cover Limited only: PremierDraft, TradDraft, PickTwoDraft, Sealed, and TradSealed per set. "Unless otherwise noted, these data sets are licensed under a Creative Commons Attribution 4.0 International License." There is no Brawl data ([Public datasets](https://www.17lands.com/public_datasets)).

### Others

- **MTGArena.pro:** no Brawl page found. The "Meta Snapshot By Archetypes" page rendered empty on 2026-10-03. Terms: "Permission is granted to temporarily download copies of the materials ... for personal, non-commercial use", and users may not "modify or copy the materials" ([Terms](https://mtgarena.pro/terms-and-conditions/)).
- **mtgdecks.net:** search results show Historic-Brawl "most played" card pages ([example](https://www.mtgdecks.net/Historic-Brawl/staples/burn)). Its robots.txt disallows `anthropic-ai`, `ClaudeBot`, `GPTBot`, and other AI agents, so this research did not read it ([robots.txt](https://mtgdecks.net/robots.txt)). Coverage, sample, and terms are **unverified**.
- **MTGAZone:** robots.txt returned HTTP 403. Not read.
- **mtgmeta.io:** no Brawl content on the home page.

## EDHREC rank against Brawl popularity

### Method

- **Cards:** read MTGGoldfish's four Brawl staples lists once, as personal use under its terms. That gives 145 nonbasic cards after basic lands are removed.
- **Commanders:** read AetherHub's Brawl and Historic Brawl commander lists once.
- **Ranks:** looked up `edhrec_rank` with Scryfall `/cards/collection`, at 4 requests per second or less, with a User-Agent.
- **Measure:** Spearman ρ between the source's share and `edhrec_rank`, with the sign set so that a positive ρ means the two orders agree.
- The raw source rows stay outside the repo, because the MTGGoldfish terms forbid reproduction.

### Results

| Comparison | Items with a rank | Spearman ρ |
|---|---|---|
| MTGGoldfish Brawl, nonland top 50 | 49 of 50 | 0.35 |
| MTGGoldfish Brawl, nonbasic lands | 45 of 45 | 0.50 |
| MTGGoldfish Brawl, all four lists | 142 of 145 | 0.41 |
| AetherHub Brawl ("Historic Brawl") commanders | 45 of 50 | 0.04 |
| AetherHub Standard Brawl ("Brawl") commanders | 8 of 8 | 0.19 (too few to judge) |

Overlap check: take the most popular Brawl-legal nonbasic cards by `edhrec_rank`, then count how many appear in MTGGoldfish's Brawl lists.

| EDHREC top N (Brawl-legal) | In MTGGoldfish lists |
|---|---|
| 25 | 17 |
| 50 | 29 |
| 100 | 44 |

### Where the orders disagree

- **Both agree on the top generic staples.** Arcane Signet (EDHREC #3), Command Tower, Swiftfoot Boots, Lightning Greaves, Counterspell, and Mind Stone lead both lists.
- **Brawl play that EDHREC ranks low:** Arena-era and two-player cards. Examples: Mox Amber, the Medallion cycle (#277 to #584), Chimil, the Inner Sun, The Aetherspark, Karn, the Great Creator, Disdainful Stroke, Essence Scatter, Luxior, Giada's Gift, Burst Lightning, Spectrum Sentinel, and Hoverstone Pilgrim.
- **Commander staples that are not in the Brawl lists:** Path to Exile, Blasphemous Act, Beast Within, Rampant Growth, Chaos Warp, Heroic Intervention, An Offer You Can't Refuse, Rhystic Study, Smothering Tithe, Cyclonic Rift, Toxic Deluge, Reanimate, and 7 of the 10 fetch lands. Many are multiplayer cards: tax effects and symmetric board wipes. "Not in the lists" means below the 7% to 9% cutoff in a sample of about 100 decks.
- **Commanders do not agree.** AetherHub's most played Brawl commander, Hei Bai, Forest Guardian, has EDHREC rank 9119. Ragavan, Nimble Pilferer and Tatyova, Benthic Druid have the best EDHREC ranks in the list, with 3 and 2 decks.
- **Null ranks:** 5 of AetherHub's top 50 Brawl commanders have no `edhrec_rank`. Two are banned in Commander: Golos, Tireless Pilgrim, and Rofellos, Llanowar Emissary. Two are Arena-only digital cards: Teysa of the Ghost Council and Anina, Natural Parallelist. The Alchemy name "A-Vivi Ornitier" did not resolve by exact name on Scryfall. In the MTGGoldfish lists, 3 of 145 cards have no rank, and all 3 are Arena-only digital cards (Spirited Simulacrum, Faceless Agent, Kami of Bamboo Groves).

### Limits of the comparison

- The samples are small: about 100 decks for MTGGoldfish and about 310 for AetherHub.
- Each MTGGoldfish list stops at 50 rows. The cut narrows the range and lowers ρ.
- MTGGoldfish decks are user-submitted. AetherHub shares count decks, not games.
- There is no per-card Standard Brawl source to compare.

## Open and unverified points

- Whether Untapped's CSV export needs a Premium account. Checking needs an account; this research made none.
- The update frequency of Untapped, AetherHub, and MTGGoldfish. None of their pages states it.
- Archidekt's Brawl format ids. They come from third-party code, not from Archidekt.
- mtgdecks.net and MTGAZone coverage and terms. Neither was read.
- Whether any source grants permission to store derived ranks. Nobody asked.

## Access log

- This research read each page once, at a human pace. It took no deck data or API data from Moxfield, Untapped, or Archidekt.
- For Moxfield, it fetched the HTML and the public JS bundle files once, to find the terms text and the format table. It did this before it read the terms.
- Scryfall: about 25 API calls, within the rate limits.
