# ManaBox CSV export schema

Research · 2026-08-24

**Question:** Pin down the exact ManaBox CSV export schema: column names and order, how multi-face/split card names are written, foil/language/condition encoding, whether the Scryfall ID column is printing-level, and any quirks that affect name-based resolution to `oracle_id`. Surface the facts the collection import parser and quarantine report depend on.

Verified against the official ManaBox guide, real export files in public repos, the Scryfall API, and a local analysis of the Oracle Cards bulk snapshot of 2026-08-24. Each claim cites its source; unverifiable points are listed at the end.

## TL;DR

- **Parse by header, not position.** There is a stable 15-column core; observed exports variously prepend `Binder Name,Binder Type` and append `Added`, so 15-, 16-, 17-, and 18-column exports all exist in the wild. A whole-collection export can arrive without the Binder columns, so they are not a marker of export scope. Header row is always present; RFC 4180 quoting; UTF-8 without BOM.
- **Multi-face names are full Scryfall names** — `Front // Back` with space-slash-slash-space for transform, MDFC, split, and adventure alike. They match the Oracle Cards `name` field exactly; no front-face-only handling is needed.
- **`Scryfall ID` is the printing-level `id`** (a Scryfall card object per set/collector number), not `oracle_id` — and it is the *English* printing even for foreign-language rows. It usually does **not** appear in the Oracle Cards bulk (which keeps one chosen printing per oracle identity), so name-based resolution against the corpus is the right primary path; keep the raw id as JSONB provenance.
- **Encodings:** Foil = `normal`/`foil`/`etched`; Condition = snake_case Cardmarket-style scale (`mint`, `near_mint`, `excellent`, `good`, `light_played`, `played`, `poor` — note `light_played`, not `lightly_played`); Language = Scryfall-style codes except Chinese (`zh_CN`/`zh_TW` where Scryfall uses `zhs`/`zht`); Misprint/Altered = `false`/`true` or blank.
- **Quarantine drivers:** token rows (ManaBox exports scanned tokens; 222 names in Oracle Cards are ambiguous by name alone, nearly all tokens — `Elemental` appears 31 times), Un-set oddities (`_____ _____ _____ Trespasser`), and set-code casing (ManaBox uppercases Scryfall's lowercase codes — irrelevant to name-only matching but relevant to provenance).

## Column names and order

Four header variants observed, all supersets of the same 15-column core:

**15-column core** — verbatim:

```
Name,Set code,Set name,Collector number,Foil,Rarity,Quantity,ManaBox ID,Scryfall ID,Purchase price,Misprint,Altered,Condition,Language,Purchase price currency
```

Identical across independent real samples: [StepKie/MtgCsvHelper reference CSV](https://github.com/StepKie/MtgCsvHelper/blob/master/MtgCsvHelper/Resources/SampleCsvs/Reference/manabox.csv), [erichschroeter/ijw2pmtg](https://github.com/erichschroeter/ijw2pmtg/blob/main/examples/ManaBox.collection.exported.csv), [gurleyryan/SparkRoot](https://github.com/gurleyryan/SparkRoot/blob/main/data/user-data/manabox-collection.csv), [Mr-Bowtie/iron_tusk_toolbox](https://github.com/Mr-Bowtie/iron_tusk_toolbox/blob/main/example_data/manabox_example.csv).

**16-column whole-collection export, no Binder columns** — the core suffixed with `Added` — verbatim from a first-party export dated 2026-08-29, default filename `ManaBox_Collection.csv`:

```
Name,Set code,Set name,Collector number,Foil,Rarity,Quantity,ManaBox ID,Scryfall ID,Purchase price,Misprint,Altered,Condition,Language,Purchase price currency,Added
```

Exported from the collection tab, from a collection whose 100 cards sit in a deck and in no binder or list — yet the [guide](https://www.manabox.app/guides/collection/import-export/) says a whole-collection export "will include all card properties as well as the binder/list name". The guide documents a single-binder/list CSV export too, so the Binder columns mark neither scope nor completeness.

**17-column whole-collection export** — the core prefixed with `Binder Name,Binder Type` — verbatim in real collection exports [nwchinn/mtg-agent](https://github.com/nwchinn/mtg-agent/blob/main/data/Ex_ManaBox_Collection.csv) and [DrakeWood/MTG-Collection-Toolkit](https://github.com/DrakeWood/MTG-Collection-Toolkit/blob/main/Manabox%20Remove%20Decks/sample_manabox_collection.csv).

**18-column whole-collection export** — additionally suffixed with `Added` — verbatim from a real export dated 2026-08-13 (default filename `ManaBox_Collection.csv`), [massimilianobotticelli/magic-strategist](https://github.com/massimilianobotticelli/magic-strategist/blob/main/data/manabox/2026-08-13/ManaBox_Collection.csv), corroborated by [scryme's importer](https://github.com/Leyline-Coding/scryme/blob/main/backend/src/importers/manabox.py):

```
Binder Name,Binder Type,Name,Set code,Set name,Collector number,Foil,Rarity,Quantity,ManaBox ID,Scryfall ID,Purchase price,Misprint,Altered,Condition,Language,Purchase price currency,Added
```

The [official guide](https://www.manabox.app/guides/collection/import-export/) confirms the whole-collection export "will include all card properties as well as the binder/list name" but publishes no column order — the headers above come from the samples. The guide's *import* column list (Name or Scryfall ID required, plus Set code/Set name; Quantity, Foil, Collector number, Language, Condition, Purchase price, currency, Misprint, Altered optional) is a subset of the export schema — every export re-imports.

Verbatim sample rows (17-column, [DrakeWood sample](https://github.com/DrakeWood/MTG-Collection-Toolkit/blob/main/Manabox%20Remove%20Decks/sample_manabox_collection.csv); 15-column, [StepKie sample](https://github.com/StepKie/MtgCsvHelper/blob/master/MtgCsvHelper/Resources/SampleCsvs/Reference/manabox.csv)):

```
White Creatures,binder,Abbey Matron,HML,Homelands,2a,normal,common,1,39299,158caa84-da2e-4c4c-b24d-0c035c900e20,0.06,false,false,near_mint,en,USD
Earth Shakers,deck,Abrade,VOW,Innistrad: Crimson Vow,139,normal,common,3,64344,a0e47d11-cb21-402b-a39e-588a94cc57b4,0.1,false,false,near_mint,en,USD
"Isperia, Supreme Judge",GK2,RNA Guild Kit,1,foil,mythic,1,,ff875d47-b46e-481e-8fba-cc90870483e0,,,,near_mint,en,
Demonic Tutor,PLST,The List,DDC-49,normal,uncommon,1,,77be13ed-5ef5-4452-a16f-ccd3599d8555,,,,near_mint,en,
```

## Multi-face card names

Full `Front // Back`, space-slash-slash-space, for **every** multi-face layout — never front-face-only:

- Transform: `Ambitious Farmhand // Seasoned Cathar`; adventure: `Brazen Borrower // Petty Theft`; split: `Fire // Ice` ([StepKie sample](https://github.com/StepKie/MtgCsvHelper/blob/master/MtgCsvHelper/Resources/SampleCsvs/Reference/manabox.csv)); split: `Depose // Deploy` ([SparkRoot export](https://github.com/gurleyryan/SparkRoot/blob/main/data/user-data/manabox-collection.csv)); 2026-set examples `Abigale, Poet Laureate // Heroic Stanza` ([magic-strategist export](https://github.com/massimilianobotticelli/magic-strategist/blob/main/data/manabox/2026-08-13/ManaBox_Collection.csv)).
- This is exactly Scryfall's top-level `name` for multi-face cards — confirmed against the API: `Fire // Ice` (layout `split`), `Delver of Secrets // Insectile Aberration` (layout `transform`) ([api.scryfall.com/cards/named](https://api.scryfall.com/cards/named?exact=Fire%20//%20Ice), checked 2026-08-24). Each such full name appears exactly once in the Oracle Cards bulk (local bulk analysis, 2026-08-24 snapshot).

So the resolver needs a single exact-match strategy: ManaBox `Name` == Oracle Cards `name`. A face-name fallback (`Fire` alone) is only needed for non-ManaBox inputs like hand-typed decklists.

## Value encodings

| Column | Values observed in real exports | Notes |
|---|---|---|
| `Foil` | `normal`, `foil`, `etched` | lowercase; matches Scryfall's finish vocabulary |
| `Condition` | `mint`, `near_mint`, `good`, `light_played`, `played`, `poor` | snake_case; `excellent` completes the 7-step Cardmarket-style scale — attested by [parser mappings](https://github.com/WonderPanda/mana-vault/blob/main/packages/api/src/parsers/manabox.ts) and [MythicHub's ManaBox condition table](https://mythichub.com/help/importing-collection?section=importing-from-manabox) ("Mint, Near Mint, Excellent, Good, Light Played, Played, Poor") but not seen verbatim in a sample. It is `light_played`, **not** `lightly_played` |
| `Language` | `en`, `es`, `fr`, `de`, `it`, `pt`, `ja`, `ko`, `ru`, `zh_CN`, `zh_TW` | Scryfall-style codes **except Chinese**: Scryfall uses `zhs`/`zht` |
| `Misprint`, `Altered` | `false`, or blank (15-col samples) | boolean strings; `true` inferred, not observed |
| `Rarity` | `common`, `uncommon`, `rare`, `mythic`, `special` | lowercase, Scryfall rarity strings |
| `Quantity` | positive integer | one row per distinct printing+finish+language+condition; the same card recurs across rows/binders |
| `ManaBox ID` | integer (e.g. `39299`) or blank | app-internal id, no external meaning |
| `Purchase price` | plain decimal (`0.11`) or blank | currency in separate column |
| `Purchase price currency` | `USD`, `EUR`, or blank | ISO 4217 |
| `Binder Name` / `Binder Type` | free text / `binder`, `deck`, `list` | 17/18-col variants only |
| `Added` | ISO 8601 UTC with ms, `2026-07-11T18:44:33.509Z` | 16/18-col variants |
| `Collector number` | `149`, `2a`, `VS`, `DDC-49` | **string, not numeric** |

Sources: value inventories from the sample CSVs cited above; condition/language mappings also in [MtgCsvHelper appsettings](https://github.com/StepKie/MtgCsvHelper) (`zhs`→`zh_CN`, `zht`→`zh_TW`, LightlyPlayed→`light_played`).

## Scryfall ID is printing-level (and English)

- Scryfall docs: `id` is "A unique ID for this card in Scryfall's database" — per printing; `oracle_id` is "consistent across reprinted card editions, and unique among different cards with the same name (tokens, Unstable variants, etc)" ([Card Objects](https://scryfall.com/docs/api/cards)).
- Resolving sample values through the API confirms printing-level: `e768c957-…` → Lightning Bolt, M11 #149, `lang: en`; `cfcf6efe-…` → Clue token, TMH2 #14 (API lookups, 2026-08-24).
- **Language quirk:** the StepKie sample has eleven Lightning Bolt rows differing only in `Language`, all with the *same* Scryfall ID — the English M11 printing. ManaBox does not use Scryfall's per-language card objects; language lives only in the `Language` column.
- **Corpus consequence:** the Oracle Cards bulk contains "one Scryfall card object for each Oracle ID … the most up-to-date recognizable version" ([bulk-data API description](https://api.scryfall.com/bulk-data), fetched 2026-08-24). A ManaBox row's printing id therefore usually is not the printing Oracle Cards happened to choose — id-lookup against the oracle corpus **misses by design**. Name matching is the primary resolution path; store the raw id in the JSONB provenance for a future printing-aware effort (it resolves exactly via Default Cards or the `/cards/{id}` API).

## Quirks the parser and quarantine report depend on

Local analysis of the Oracle Cards bulk (2026-08-24 snapshot: 38,626 card objects, of which 1,050 tokens):

1. **Tokens are in the export.** ManaBox scans tokens and exports them as plain names with Scryfall token set codes (`Clue`/TMH2, `Food`/TLTR in the StepKie sample; 146 TMT rows in the magic-strategist export). Tokens *do* have oracle identities and appear in Oracle Cards — but **222 names in the bulk are ambiguous** (appear on >1 oracle identity), nearly all token names: `Elemental` ×31, `Spirit` ×23, `Soldier` ×13, `Bird` ×13. Name-only resolution must treat multi-hit names as unresolvable → quarantine, not pick-first. (`Clue` and `Food` happen to be unique today; `Elemental` never will be.)
2. **Set codes are Scryfall's, uppercased.** `M11`, `PLST`, `TMH2` vs Scryfall's `m11`, `plst`, `tmh2`. The official guide states "The set names and codes supported are the same ones from Scryfall" ([guide](https://www.manabox.app/guides/collection/import-export/)). Compare case-insensitively if set code is ever used as a disambiguator or in quarantine display.
3. **CSV mechanics:** header row always present; comma delimiter; names containing commas are double-quoted per RFC 4180 (`"Isperia, Supreme Judge"`); all samples are UTF-8 without BOM with non-ASCII kept verbatim (`Bespoke Bō`) — normalize to NFC before matching, since the corpus is also NFC; blank cells are legal in `ManaBox ID`, `Purchase price`, `Misprint`, `Altered`, `Condition`, and currency columns; line endings vary across samples (CRLF/LF) — accept both, though a raw export straight out of the app is CRLF with no trailing newline after the last row, so the LF samples are git-normalized.
4. **Oracle-level dedupe is the importer's job.** The same oracle identity recurs across many rows (per binder, finish, language, condition). Oracle quantity = sum of `Quantity` over all rows resolving to the same `oracle_id`.
5. **Un-set / oddball names** occur in real collections (`_____ _____ _____ Trespasser`, UNF; Mystery Booster playtest cards, CMB1) — they exist in Oracle Cards under exactly those names, so they resolve, but they are natural quarantine candidates if the corpus build ever filters layouts.
6. **Reversible cards are a non-issue for this corpus.** Scryfall docs note `oracle_id` is absent at top level for `layout: reversible_card` (it sits on each face) — but the 2026-08-24 Oracle Cards bulk contains **zero** reversible_card objects; the oracle identity is carried by the normal-layout object. Only a resolver walking Default/All Cards would need face-level handling.
7. **Quarantine UX precedent:** ManaBox's own CSV *import* reports failed lines with line numbers and offers the failures back as a downloadable error CSV ([guide](https://www.manabox.app/guides/collection/import-export/)) — the same shape as the quarantine report decided on #15.

## Unverified / uncertain

- `excellent` (condition) and `bonus` (rarity): attested by third-party parsers and MythicHub's mapping, never seen in a raw export.
- `true` for Misprint/Altered: inferred from the boolean `false`; not observed verbatim.
- Languages beyond the 11 observed (Phyrexian `ph`, `he`, `la`, …): unverified.
- Alchemy `A-`-prefixed names: no sample contained any (ManaBox is paper-scanner-centric).
- Whether export headers are ever localized: all observed headers are English.
- What drives the Binder columns: two whole-collection exports two weeks apart disagree (18-column with them on 2026-08-13, 16-column without them on 2026-08-29). Best hypothesis is "no binder or list holds any card", untested. Until it is pinned, assume any of the four variants can arrive.

The `excellent`/`bonus`, Misprint/Altered, and language entries were re-checked against the 16-column export on 2026-08-29 and **none appeared**: all 100 rows were `near_mint` / `en` / `false` / `false` / `USD`, rarities `common|uncommon|rare|mythic`, `Foil` `normal|foil`. Weak evidence — 100 uniform English near-mint singles, no tokens, no duplicate oracle identities. No parser tolerance change follows: only `Name` and `Quantity` are interpreted, so an unseen value in any other column is provenance, never a failure.

## Sources

- Official guide: [Import and export the collection — manabox.app](https://www.manabox.app/guides/collection/import-export/) (developer SkillDevs)
- Real export samples: [StepKie/MtgCsvHelper](https://github.com/StepKie/MtgCsvHelper/blob/master/MtgCsvHelper/Resources/SampleCsvs/Reference/manabox.csv), [nwchinn/mtg-agent](https://github.com/nwchinn/mtg-agent/blob/main/data/Ex_ManaBox_Collection.csv), [DrakeWood/MTG-Collection-Toolkit](https://github.com/DrakeWood/MTG-Collection-Toolkit/blob/main/Manabox%20Remove%20Decks/sample_manabox_collection.csv), [massimilianobotticelli/magic-strategist](https://github.com/massimilianobotticelli/magic-strategist/blob/main/data/manabox/2026-08-13/ManaBox_Collection.csv), [erichschroeter/ijw2pmtg](https://github.com/erichschroeter/ijw2pmtg/blob/main/examples/ManaBox.collection.exported.csv), [gurleyryan/SparkRoot](https://github.com/gurleyryan/SparkRoot/blob/main/data/user-data/manabox-collection.csv), [Mr-Bowtie/iron_tusk_toolbox](https://github.com/Mr-Bowtie/iron_tusk_toolbox/blob/main/example_data/manabox_example.csv)
- Independent parsers: [Leyline-Coding/scryme](https://github.com/Leyline-Coding/scryme/blob/main/backend/src/importers/manabox.py), [WonderPanda/mana-vault](https://github.com/WonderPanda/mana-vault/blob/main/packages/api/src/parsers/manabox.ts), [nicho92/MtgDesktopCompanion](https://github.com/nicho92/MtgDesktopCompanion/blob/master/src/main/java/org/magic/api/exports/impl/ManaBoxExport.java)
- Scryfall: [Card Objects](https://scryfall.com/docs/api/cards), [Bulk Data](https://scryfall.com/docs/api/bulk-data) + live [`/bulk-data` API](https://api.scryfall.com/bulk-data), [Languages](https://scryfall.com/docs/api/languages); Oracle Cards bulk snapshot 2026-08-24 (local analysis), API card lookups 2026-08-24
- Condition mapping corroboration: [MythicHub — importing from ManaBox](https://mythichub.com/help/importing-collection?section=importing-from-manabox)
