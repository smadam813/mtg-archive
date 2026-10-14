# Phrase-to-format mapping

Users name a format in their own words. The service takes one of four format values: `commander`, `brawl`, `competitive_brawl`, or `standard_brawl`. This table converts the words to a value. The conversion happens in the skill. The service never sees the phrase.

| Wording | Format |
|---|---|
| No format word, "Commander", "EDH" | `commander`, the default. A request that names no format means Commander. |
| "Brawl", "Historic Brawl", "Arena Brawl" (one deck) | `brawl`, the 100-card Arena format. Arena's Brawl format was formerly Historic Brawl. |
| "Competitive Brawl", "ranked Brawl" | `competitive_brawl` |
| "Standard Brawl" | `standard_brawl` |
| "Arena Brawl queues", "which queues", "every Brawl queue" | All three Brawl formats, one call per format. |
| A move ("take my Brawl deck into Competitive Brawl") | The destination is the request format. The source word only describes the deck. |
| "Arena" with no "Brawl", or a pasted Arena export with no format word | **Ask.** Offer the three Brawl formats and Commander. An export carries no format line. Do not guess from the deck size. |
| "tabletop Brawl", "paper Brawl", Standard, Historic, Timeless | Say that the format is out of scope. Make no call. |

Match without regard to case or hyphens ("competitive-brawl", "Standard brawl").

Say "format" in the answer. The user may say "queue"; match it, but do not echo it as the term.

Always state the reading in the answer, at the top, before any number it shapes. Example:

> Treating 'Brawl' as the Brawl format (Arena's Brawl format, formerly Historic Brawl).

A Commander answer states its reading too:

> No format named: treating the request as Commander.

The format stays set for later turns of the same conversation until the user names another one.

The rule for stating assumptions is in `answer-contract.md`.
