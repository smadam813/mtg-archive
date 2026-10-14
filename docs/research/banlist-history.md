# Research: Commander banlist dated version history sources

**Question**: Where can a dated version history of the Commander banlist be obtained, not just its current state?

**Date researched:** 2026-08-23

## Summary

No single official machine-readable source provides the Commander banlist as a dated version history. Current-state APIs (Scryfall, MTGJSON) carry legality only as of "now". A complete dated history is reconstructable from three layers:

1. **Official announcement archives** — dated, per-change, authoritative, but prose (HTML pages, not structured data). RC-era changes live on mtgcommander.net's blog and its Wayback Machine captures; WotC-era changes (Sept 2024 onward) live on magic.wizards.com's dated announcement pages.
2. **Community-maintained dated histories** — two independent, citation-backed timelines cover the full format history (2002–present) and agree with each other; ideal as a bootstrap dataset, cross-checked against layer 1.
3. **Wayback Machine snapshots** of the banlist pages — dated full-list states usable to verify that a reconstructed event log replays to the correct list at any point in time.

**Recommendation:** Bootstrap the dated-version-history store by transcribing the community timelines into a structured event log (date, card, action, source URL), verify each event against the official announcement it cites, and spot-check reconstructed states against Wayback snapshots. Going forward, append events from WotC's Commander B&R announcements on magic.wizards.com (no fixed cadence as of Feb 2026 — watch the announcements feed rather than a calendar).

## 1. Who has published the banlist, and when

| Era | Steward | Where changes were announced |
|---|---|---|
| ~2002 – Sept 2024 | Commander Rules Committee (RC), independent of WotC | Early years: forum posts (dragonhighlander.net, MTGSalvation); from Dec 2019: dated blog posts on mtgcommander.net |
| Sept 30, 2024 – present | Wizards of the Coast, advised by the Commander Format Panel | Dated announcements on magic.wizards.com |

- The first known Commander banlist dates to **October 2002** (synchronized with the Vintage banlist; Test of Endurance was the first EDH-specific power ban). Source: [MTG Wiki B&R timeline](https://mtg.wiki/page/Banned_and_restricted_cards/Timeline).
- On **Sept 23, 2024** the RC issued its last update (banning Dockside Extortionist, Jeweled Lotus, Mana Crypt, Nadu, Winged Wisdom): [September 2024 quarterly update](https://mtgcommander.net/index.php/2024/09/23/september-2024-quarterly-update/).
- On **Sept 30, 2024** the RC handed management of the format to WotC; WotC then created the [Commander Format Panel](https://magic.wizards.com/en/news/announcements/introducing-the-commander-format-panel) as its advisory body. Secondary confirmation: [Star City Games](https://articles.starcitygames.com/magic-the-gathering/wotc-game-design-team-to-take-over-commander-format-management/), [ICv2](https://icv2.com/articles/news/view/57919/magic-the-gathering-commander-rules-committee-fires-itself).

## 2. Official announcement archives (primary sources, per-change, dated)

### RC era (through Sept 2024)

- **mtgcommander.net blog (Dec 2019 – Sept 2024):** the RC posted dated "quarterly update" articles (e.g. [Feb 5, 2024](https://mtgcommander.net/index.php/2024/02/05/february-2024-quarterly-update/), [Apr 16, 2024](https://mtgcommander.net/index.php/2024/04/16/april-2024-quarterly-update/), [Jul 29, 2024](https://mtgcommander.net/index.php/2024/07/29/july-2024-quarterly-update/), [Sept 23, 2024](https://mtgcommander.net/index.php/2024/09/23/september-2024-quarterly-update/)). The site is still up as a static fan-content archive; its banlist page is frozen at the Sept 2024 state.
- **Pre-2019:** announcements were made on the RC's forums and MTGSalvation threads; the sites survive mainly via the Wayback Machine (see section 4). The predecessor site dragonhighlander.net (archived from 2009) even had an official `EDH_Root/changelog.php`.

### WotC era (Sept 2024 – present)

- **[magic.wizards.com news/announcements archive](https://magic.wizards.com/en/news/announcements)** — browsable, dated, with stable per-announcement URLs. Commander-specific B&R pages so far:
  - [Commander B&R – April 22, 2025](https://magic.wizards.com/en/news/announcements/commander-bans-and-restrictions-april-22-2025): unbanned Gifts Ungiven, Sway of the Stars, Braids, Cabal Minion, Coalition Victory, Panoptic Mirror; committed to no further 2025 changes.
  - [Commander B&R – February 9, 2026](https://magic.wizards.com/en/news/announcements/commander-banned-and-restricted-february-9-2026): unbanned Biorhythm and Lutri, the Spellchaser; introduced a new **"banned as a companion"** category (Lutri is its sole member).
  - Related: [Commander Brackets Beta update – Feb 9, 2026](https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-february-9-2026) — the "Game Changers" list (53 cards as of Feb 2026) is bracket-restricted, not banned; it is a separate, adjacent list a history store may also want to version.

## 3. Community-maintained dated histories (best bootstrap data)

- **[smileylich.com Commander Banlist History](https://www.smileylich.com/mtg/magocracy/Magocracy_G3.html)** — an unofficial chronological table of every banlist change from the format's origins through **February 2026**, per entry: date, cards banned, cards unbanned, Game Changers added/removed, and notes, with links to primary sources (WotC announcements, MTGSalvation forum posts). HTML table, not machine-readable, but comprehensive and easy to transcribe.
- **[MTG Wiki: Banned and restricted cards/Timeline](https://mtg.wiki/page/Banned_and_restricted_cards/Timeline)** (also mirrored at [mtg.fandom.com](https://mtg.fandom.com/wiki/Banned_and_restricted_cards/Timeline)) — all-formats timeline at year/month granularity with numbered footnote citations to the official announcements. Verified to cover Commander from the October 2002 first banlist through the Feb 2026 Lutri change, including subtleties a schema must model:
  - "banned as commander only" entries (2006–2014), e.g. Heartless Hidetsugu, Kaervek, Niv-Mizzet (Feb 2006), Rofellos (Mar 2007);
  - the **September 2014 elimination of the "banned only as commander" category** (Braids, Erayo, Rofellos became fully banned);
  - the **Dec 1, 2017 – Jan 15, 2018 time-boxed silver-bordered unbanning**;
  - preemptive class bans (Conspiracy card type, June 2014);
  - the Feb 2026 **"banned as companion"** category.
- Not applicable but similarly named: [barrins-archive/mtgdc_banlist](https://github.com/barrins-archive/mtgdc_banlist) on GitHub holds dated JSON banlist history for **Duel Commander** (a different format, French 1v1); useful only as a schema precedent.

## 4. Wayback Machine coverage (dated full-list states, for verification)

Checked via the CDX API (`web.archive.org/cdx/search/cdx`):

- `mtgcommander.net/index.php/banned-list/` — 64 monthly snapshots, Apr 2020 → Aug 2026.
- `mtgcommander.net/rules.php` (old site; banlist embedded in rules page) — 139 monthly snapshots, Dec 2010 → 2026.
- `dragonhighlander.net` (pre-2010 predecessor) — snapshots from 2009, including the official changelog page.

These give dated *states* (full lists), complementing the announcement archives' dated *events*. Replaying the event log and diffing against snapshots is a good integrity check.

## 5. What structured sources do NOT provide

- **Scryfall API:** the card object's `legalities` is "an object describing the legality of this card across play formats" with values `legal / not_legal / restricted / banned` — current state only, no history ([Scryfall API docs](https://scryfall.com/docs/api/cards)).
- **MTGJSON:** the card model's `legalities` property likewise reflects only present status; no historical banlist product ([MTGJSON docs](https://mtgjson.com/data-models/card/card-set/), see `legalities` on the Card (Set) model).
- WotC's own [banned & restricted page](https://magic.wizards.com/en/banned-restricted-list) is also current-state only.

## 6. How future updates are published

- WotC publishes Commander B&R changes as dated announcement pages under `magic.wizards.com/en/news/announcements/` (slug pattern so far: `commander-banned-and-restricted-<date>` / `commander-bans-and-restrictions-<date>`).
- Cadence: 2025 had a single Commander window (April 22). The Feb 9, 2026 announcement states they are "not limiting ourselves to just one banned and restricted update window in a year" — i.e. **no fixed schedule**; consumers should watch the announcements feed, not a calendar. General (non-Commander) B&R announcements can also touch Commander in principle, so the whole announcements feed is the thing to monitor.
- The Brackets/Game Changers list changes on the same announcement channel and should be versioned alongside if the archive models it.

## 7. Recommendation for the banlist-as-dated-version-history store

1. **Model events, not states:** `(effective_date, card, action, scope, source_url)` where `action` ∈ {banned, unbanned} and `scope` handles the historical categories: fully banned, banned-as-commander-only (pre-Sept-2014), banned-as-companion (Feb 2026+), plus class bans (Conspiracy types, silver-border with an expiry window).
2. **Bootstrap** by transcribing the smileylich table cross-checked against the MTG Wiki timeline; both cite official announcements — record the cited announcement URL (or Wayback URL for dead RC/forum links) as each event's source.
3. **Verify** reconstructed list states against Wayback snapshots of mtgcommander.net (2010→2024) and against the current WotC/mtgcommander lists.
4. **Ingest forward** from the magic.wizards.com announcements feed; treat Scryfall/MTGJSON legality as a current-state consistency check only.

Granularity note: exact day-level dates are recoverable from ~2010 onward (announcement posts are dated to the day); for 2002–2009 some events are recoverable only to the month the announcement thread was posted, and the store should tolerate month-precision dates for that era.
