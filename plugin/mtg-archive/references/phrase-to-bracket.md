# Phrase-to-bracket mapping

Users describe power level in words. The service takes a bracket number. This table converts one to the other. The conversion happens in the skill. The service never sees a power-level phrase.

This mapping applies to Commander only. Read the format first with `phrase-to-format.md`. In a Brawl format, power words set nothing, and you make no bracket call. State in the answer: "No power target: Arena publishes no bracket system for Brawl." The one exception: "Competitive Brawl" or "ranked Brawl" picks the `competitive_brawl` format.

| Phrase | Bracket |
|---|---|
| `precon` | 2 |
| `jank` | 2 |
| `theme` | 2 |
| `casual` | 2 |
| `upgraded` | 3 |
| `mid` | 3 |
| `focused` | 3 |
| `competition-ready` | 4 |
| `high-power` | 4 |
| `optimized` | 4 |
| `cEDH` | 5 |

Match a phrase without regard to case or hyphens ("high power", "High-Power").

If the user's phrasing matches no row, ask the user which bracket they mean. Do not guess.

Always state the mapping you used in the answer, next to the bracket it shaped. Example:

> Treating "competition-ready" as bracket 4.

The rule for stating assumptions is in `answer-contract.md`.
