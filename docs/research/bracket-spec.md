# Research: Commander Brackets (beta) spec and the Game Changers list

**Question**: Where are the WotC Commander Brackets (beta) spec and the Game Changers
list published, how are they versioned and revised, and what inputs does bracket classification
require? Enough to later design how "competition-ready" resolves to a bracket — the vision requires
this to share no code path with legality and never be treated as deterministic.

Researched 2026-08-23. All claims cite the source that states them; sources are listed at the end.

## Summary

- The spec and the Game Changers list are published by Wizards of the Coast in two places: a
  **standing canonical page** (the Commander format page at
  `magic.wizards.com/en/formats/commander`, which carries the five bracket definitions and the full
  Game Changers card list) and **dated announcement articles** ("Commander Brackets Beta Update –
  {date}") that function as the changelog [S1, S2–S5].
- There is **no formal version scheme and no machine-readable publication from WotC**. Versioning
  is "by dated article"; the standing page is updated in place with no visible last-updated marker
  or change history [S1, S2–S5]. Scryfall provides the de facto machine-readable mirror: a
  `game_changer` boolean on every card object [S7].
- The system is still **in beta** (as of the 2026-02-09 update) and WotC has said structural
  changes remain possible; revisions have so far landed roughly quarterly, timed around MagicCons
  [S4, S5].
- Bracket classification mixes **decklist-derivable signals** (Game Changers count, mass land
  denial, chained extra turns, two-card infinite combos) with **inputs that cannot be derived from
  a decklist** (player intent, theme, expected game length, metagame orientation). WotC states
  outright that "intent is the most important part of the bracket system" and that deck-building
  calculators provide **estimates only** [S1, S3]. Any implementation that outputs a single
  authoritative bracket number contradicts the publisher of the spec.

## 1. Where it is published

### Canonical standing page

`https://magic.wizards.com/en/formats/commander` — the official Commander format page. Verified
2026-08-23 to contain, in page content [S1]:

- The framing: "Brackets are an entirely optional way to help matchmake your Commander games with
  people [who] have similar intents. … Each bracket has an intent and philosophy behind it, which
  is the most important part. There is also a list of what is expected at that level in a few
  barometers: two-card infinite combos, extra turns, mass land denial."
- The five bracket definitions with per-bracket "Players expect:" text and turn expectations
  (quoted in section 3).
- The full Game Changers card list (verified by card names such as Ad Nauseam and Drannith
  Magistrate appearing in the page HTML) and the per-bracket limits: "Bracket 1 and 2 decks exclude
  Game Changers. Bracket 3 allows for up to three Game Changers. Brackets 4 and 5 allow for
  unlimited Game Changers."
- "Commander Brackets are currently in beta" with a Discord link for feedback.

There is **no** dedicated URL such as `/formats/commander/brackets` (returns 404, checked
2026-08-23). The format page is the single standing home.

### Dated announcement articles (the changelog)

Revisions are announced in articles on `magic.wizards.com/en/news/announcements`, each written by
Gavin Verhey on behalf of the Commander Format Panel (WotC took over Commander format management
from the Commander Rules Committee in late 2024 [S6]):

| Date | Article | Effect |
|---|---|---|
| 2025-02-11 | Introducing Commander Brackets Beta [S2] | Initial spec: 5 brackets, ~40-card Game Changers list, barometers (Game Changers, two-card infinite combos, chained extra turns, mass land denial, tutor frequency) |
| 2025-04-22 | Brackets Beta Update [S3] | Game Changers: −2 (Trouble in Pairs, Trinisphere), +18 (Teferi's Protection, Humility, Narset Parter of Veils, Intuition, Consecrated Sphinx, Necropotence, Orcish Bowmasters, Notion Thief, Deflecting Swat, Gamble, Worldly Tutor, Crop Rotation, Seedborn Muse, Natural Order, Food Chain, Aura Shards, Field of the Dead, Mishra's Workshop). Reaffirmed intent-first framing; calculators are estimates only |
| 2025-10-21 | Brackets Beta Update [S4] | Game Changers: −10 (incl. Expropriate, Jin-Gitaxias, Sway of the Stars, Vorinclex Voice of Hunger, Kinnan, Urza Lord High Artificer, Winota, Yuriko, Deflecting Swat, Food Chain) → 51 cards. **Structural changes**: Bracket 2 decoupled from "precon power"; tutor restrictions eliminated as a barometer (the Game Changers list absorbs that role); per-bracket turn expectations added (9+/8+/6+/4+/any) |
| 2026-02-09 | Brackets Beta Update [S5] | Game Changers: +2 (Farewell, Biorhythm — the latter added on unban) → 53 cards. Lutri unbanned but deliberately *not* added. No bracket-level changes; next check-in signalled for ~May–June 2026 |

No later update was found as of 2026-08-23 (the promised mid-2026 check-in has not appeared in
search results), and Scryfall's live count of 53 matches the 2026-02-09 state [S7-api].

## 2. How it is versioned and revised

- **No version numbers.** Neither the standing page nor the articles carry a version identifier.
  The only usable version key is the **announcement date** — the same convention the vision doc
  already adopts for the banlist ("dated version history, not current state").
- **Standing page mutates in place.** The format page shows no last-updated date and no changelog
  [S1]. Reconstructing "the list as of date X" requires the article history above (or a third-party
  archive; the MTG Wiki page Scryfall itself links to keeps a change history [S7]).
- **Cadence.** Revisions have landed roughly every 3–6 months, explicitly timed around MagicCon
  feedback cycles. The 2026-02-09 article commits to "fairly regular updates" resuming ~May–June
  2026 and says the panel expects future Game Changers changes to be "in the order of a few cards
  rather than ten" [S4, S5].
- **Beta status.** Still beta as of 2026-02-09; WotC has said it will "likely drop the beta tag
  later this year" (2026) and reserved the right to make structure-level changes [S5]. October 2025
  proved that revisions can change the *shape* of the spec (barometers added/removed), not just
  list membership [S4].
- **Bans are a separate publication.** Card legality changes ship in separate "Commander Banned and
  Restricted Announcement" articles (e.g. 2026-02-09 [S8]), on a separate page section, on their
  own cadence. Game Changers are all *legal* cards; the list is described as a signal (and informal
  watch-list), never as a legality mechanism [S2]. Brackets appear nowhere in the Comprehensive
  Rules definition of the format. **The publisher itself keeps brackets and legality on separate
  tracks — the vision's "no shared code path" requirement mirrors the source of truth.**

## 3. The spec as currently published (2026-02-09 state)

Five brackets. 1–3 are "different levels of socially focused play"; 4–5 are "focused on a higher
power or even a competitive experience" [S1]. Per the standing page [S1]:

| # | Name | Game Changers | Expected game length | Character (abridged from "Players expect") |
|---|---|---|---|---|
| 1 | Exhibition | 0 | ≥ 9 turns | Theme over power; win conditions "highly thematic or substandard"; legality rules may even flex by pod agreement |
| 2 | Core | 0 | ≥ 8 turns | Unoptimized, straightforward decks; incremental, telegraphed, disruptable win conditions; low-pressure social play |
| 3 | Upgraded | ≤ 3 | ≥ 6 turns | Strong synergy and card quality; win from hand in one big turn off accrued resources |
| 4 | Optimized | unlimited | ≥ 4 turns | Lethal, consistent, fast; efficient/instantaneous wins; not the cEDH metagame |
| 5 | cEDH | unlimited | any turn | Built against the cEDH metagame; victory prioritized over all else |

Barometers referenced by the brackets (beyond the Game Changers list): **two-card infinite
combos, extra turns, mass land denial** [S1]. Lower brackets exclude or limit these (e.g. no mass
land denial in brackets 1–3, no/late two-card combos below bracket 4) [S2]. Tutor frequency was a
barometer in the original spec but was **removed** in October 2025 [S4] — a caution against
hard-coding barometer semantics.

The Game Changers list itself: 53 cards as of 2026-02-09, defined as "cards that can dramatically
warp Commander games" — resource runaways, play-blocking pieces, efficient tutors, and commanders
that pull games away from casual play [S1]. Full current list in the appendix.

## 4. What inputs bracket classification requires

Two classes of input, per the spec's own text:

**Derivable from a decklist** (mechanical checks):
1. **Game Changers count** — set intersection of the 99+commander against a *dated* Game Changers
   snapshot. The only crisply specified numeric rule (0 / 0 / ≤3 / ∞ / ∞) [S1].
2. **Mass land denial presence** — no official card list exists; category judgment [S1, S2].
3. **Chained/looped extra turns** — no official card list; category judgment [S1, S2].
4. **Two-card infinite combos** (and whether they can fire "early") — no official list; the
   community database (Commander Spellbook) is unofficial. The original spec qualifies this with
   *intentionality* — an accidental combo does not bump a deck's bracket [S2].

**Not derivable from a decklist** (must come from the user or be estimated with stated uncertainty):
5. **Intent** — "intent is the most important part of the bracket system" [S3]; each bracket "has
   an intent and philosophy behind it, which is the most important part" [S1].
6. **Theme vs. optimization** — bracket 1 vs. 2 vs. 3 is mostly this axis [S1].
7. **Expected game length / speed to win** — the turn expectations are experiential ("you should
   expect to be able to play at least N turns"), not decklist properties, though simulation can
   estimate them [S1].
8. **Metagame orientation** — bracket 5 is defined by *building against the cEDH metagame*, not by
   card contents; bracket 4 explicitly excludes that posture [S1].

WotC's own statement on tooling: deck-building calculators "provide estimates only" [S3]. A
decklist alone can prove a deck is *not below* some bracket (e.g. 5 Game Changers ⇒ at least
bracket 4) but can never prove which bracket it *is*.

## 5. Machine-readable sources

- **WotC publishes none.** No API, no JSON, no feed. The list lives in page prose [S1].
- **Scryfall** (de facto standard mirror): every card object carries a nullable Boolean
  `game_changer` — "True if this card is on the Commander Game Changer list" [S7]. Queryable via
  `is:gamechanger` (53 results, verified 2026-08-23 [S7-api]) and present in Scryfall bulk data.
  Note Scryfall exposes only *current* membership — no history — and it is a third party tracking
  WotC announcements, not the publisher.
- Card **legality** is a separate Scryfall field (`legalities.commander`), reinforcing that the two
  concerns never share a data path even in the ecosystem's main data source [S7].

## 6. Implications for mtg-archive (recommendation)

1. **Snapshot the Game Changers list as dated versions**, exactly like the banlist in the vision
   doc. Key: announcement date. Seed from the article history in section 1; keep current via the
   Scryfall `game_changer` flag in bulk data, diffing against the latest stored snapshot and
   recording a new dated snapshot when it changes (attribute it to the WotC announcement, which is
   the primary source).
2. **Model bracket resolution as an advisory classifier, never a validator.** Output an *estimated*
   bracket (or better: a floor — "cannot be below bracket N because of X") plus the reasons, plus
   the non-derivable inputs (intent, expected speed, metagame posture) it assumed or needs from the
   user. This is not a design nicety — it is what the spec's publisher says the system is.
3. **Keep it out of the legality path entirely.** Legality = per (card, role, banlist version),
   deterministic, boundary-filtered. Bracket = per (decklist, Game Changers version, stated
   intent), probabilistic, scoring/advice-layer. WotC publishes them separately, revises them
   separately, and defines them differently; the vision's constraint is the correct reading of the
   primary sources.
4. **Expect spec drift, not just list drift.** October 2025 removed a barometer (tutors) and added
   one (turn expectations). Barometer definitions (mass land denial, extra turns, combos) should be
   data/config, not code, and tagged with the spec date they implement. The beta tag is expected to
   drop during 2026, likely with further changes [S5].
5. **The fuzzy barometers need their own curated data.** Mass land denial, extra-turn chains, and
   two-card combos have no official card lists. Either curate small tagged lists (versioned like
   everything else) or treat those checks as heuristic with confidence attached; Commander
   Spellbook is a usable but unofficial combo source.

## Appendix: Game Changers list, 2026-02-09 revision (53 cards)

Snapshot retrieved 2026-08-23 via Scryfall `is:gamechanger` [S7-api]; membership originates from
the WotC announcements [S2–S5] and the standing page [S1].

Ad Nauseam; Ancient Tomb; Aura Shards; Biorhythm; Bolas's Citadel; Braids, Cabal Minion; Chrome
Mox; Coalition Victory; Consecrated Sphinx; Crop Rotation; Cyclonic Rift; Demonic Tutor; Drannith
Magistrate; Enlightened Tutor; Farewell; Field of the Dead; Fierce Guardianship; Force of Will;
Gaea's Cradle; Gamble; Gifts Ungiven; Glacial Chasm; Grand Arbiter Augustin IV; Grim Monolith;
Humility; Imperial Seal; Intuition; Jeska's Will; Lion's Eye Diamond; Mana Vault; Mishra's
Workshop; Mox Diamond; Mystical Tutor; Narset, Parter of Veils; Natural Order; Necropotence; Notion
Thief; Opposition Agent; Orcish Bowmasters; Panoptic Mirror; Rhystic Study; Seedborn Muse; Serra's
Sanctum; Smothering Tithe; Survival of the Fittest; Teferi's Protection; Tergrid, God of Fright;
Thassa's Oracle; The One Ring; The Tabernacle at Pendrell Vale; Underworld Breach; Vampiric Tutor;
Worldly Tutor

## Sources

- [S1] Commander format page (canonical standing spec + list): https://magic.wizards.com/en/formats/commander — fetched 2026-08-23
- [S2] "Introducing Commander Brackets Beta", Gavin Verhey, 2025-02-11: https://magic.wizards.com/en/news/announcements/introducing-commander-brackets-beta
- [S3] "Commander Brackets Beta Update – April 22, 2025": https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-april-22-2025
- [S4] "Commander Brackets Beta Update – October 21, 2025": https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-october-21-2025
- [S5] "Commander Brackets Beta Update – February 9, 2026": https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-february-9-2026
- [S6] "On the Future of Commander" (WotC assumes format management): https://magic.wizards.com/en/news/announcements/on-the-future-of-commander
- [S7] Scryfall API card object docs (`game_changer` field): https://scryfall.com/docs/api/cards — fetched 2026-08-23
- [S7-api] Scryfall search API, `is:gamechanger` (53 results): https://api.scryfall.com/cards/search?q=is%3Agamechanger — fetched 2026-08-23
- [S8] "Commander Banned and Restricted Announcement – February 9, 2026" (separate legality track): https://magic.wizards.com/en/news/announcements/commander-banned-and-restricted-february-9-2026
