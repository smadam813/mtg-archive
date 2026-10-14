# Comprehensive Rules and mechanics corpus sources

Question: where do the Magic Comprehensive Rules (CR) and
mechanics/keyword documentation live in machine-ingestable form — file formats, URL
stability, update cadence, internal structure as it bears on chunking and
rule-number-level citation, and supplementary mechanics documentation.

All facts verified 2026-08-23 against the live sources cited inline.

## Summary / recommendation

- **Ingest the official CR plain-text (TXT) file.** It is UTF-8, line-oriented, and
  every rule, subrule, and glossary entry is mechanically parseable with a handful of
  regexes. Chunk at the rule (`100.1.`) level with subrules and examples attached, plus
  one chunk per glossary term. Rule numbers are the natural citation key.
- **Do not hotlink the dated media.wizards.com URL.** Discover the current TXT link by
  scraping the stable landing page `https://magic.wizards.com/en/rules`, download, and
  archive each version in our own storage keyed by effective date. Filenames are dated
  per release, have varied in format over the years, and a file can be silently
  replaced under a *new* date mid-release (observed: the Aug 7, 2026 TXT now 404s,
  superseded by an Aug 19 re-upload).
- **Refresh on set releases**: the CR changes with each premier set (~5-6 times/year),
  announced in per-set "Update Bulletin" articles on magic.wizards.com.
- **Mechanics documentation is inside the CR itself** (rule 701 Keyword Actions,
  702 Keyword Abilities, and the 739-term glossary). Supplement with Scryfall's
  machine-readable keyword catalogs (for enumerating keywords and linking them to
  cards) and Scryfall's official-rulings bulk file. The community Academy Ruins API is
  useful as a parsed-CR/diff/renumbering reference but should not be the primary
  ingestion path.

## 1. The official Comprehensive Rules files

### Formats and URLs

The canonical distribution point is **https://magic.wizards.com/en/rules** ("You can
download the most recent version from the Magic rules website at
Magic.Wizards.com/Rules" — stated in the CR's own introduction). As of 2026-08-23 the
page links three formats of the current CR:

| Format | URL (current) | Notes |
| --- | --- | --- |
| TXT  | `https://media.wizards.com/2026/downloads/MagicCompRules 20260819.txt` | UTF-8 with BOM, CRLF, ~978 KB, 9.4k lines. **Best for ingestion.** |
| PDF  | `https://media.wizards.com/2026/downloads/MagicCompRules 20260807.pdf` | Same content, layout-oriented. |
| DOCX | `https://media.wizards.com/2026/downloads/MagicCompRules 20260807.docx` | Same content, Word. |

There is no official API, no JSON, and no official changelog file — just these three
files replaced release by release.

### URL stability (verified by direct HTTP checks, 2026-08-23)

- URLs follow `https://media.wizards.com/{year}/downloads/MagicCompRules {YYYYMMDD}.{ext}`
  (note the literal space, `%20` when encoded). The embedded date is the release date
  of the file, **not always the effective date**, and the naming has drifted over the
  years — e.g. `MagicComp Rules 20230203.txt` (2023, extra space inside the name)
  vs `MagicCompRules 20200417.txt` (2020). A scraper must not assume one fixed pattern.
- Old versions generally stay live for years: the 2020-04-17, 2023-02-03, and
  2025-06-06 TXT files all still return HTTP 200.
- **But files are replaced without redirects when errata land.** The current release
  is effective Aug 7, 2026; its PDF/DOCX carry date 20260807, while the TXT was
  re-uploaded as 20260819 and the original `MagicCompRules 20260807.txt` now returns
  404. (Academy Ruins' redirect still pointed at the dead 20260807 TXT when checked —
  even dedicated trackers get bitten.)

**Implication:** treat the landing page as the only stable entry point; scrape its
`.txt` link each refresh; store every downloaded version ourselves (keyed by the
"effective as of" date parsed from the file header) so citations remain reproducible
even after WotC replaces or removes a file.

### Update cadence and versioning

- The file self-identifies its version in line 3: `These rules are effective as of
  August 7, 2026.` That date is the version identifier; there is no numeric version.
- Updates ship alongside each premier/tabletop set release — roughly every 6-10 weeks,
  ~5-6 per year. Each set gets an "Update Bulletin" news article on
  magic.wizards.com summarizing CR changes (e.g. Lorwyn Eclipsed, Jan 2026: new
  Sorcerer subtype; Marvel Super Heroes, Jun 2026: new rules 702.193 power-up and
  702.194 teamwork; The Hobbit, Aug 2026: rule 605.1a list change). These bulletins
  are the official change log and are worth ingesting as change-context, but they are
  prose articles, not structured data.
- Between-set hotfix re-uploads happen (the 20260819 TXT above) without any bulletin.

## 2. Internal structure of the TXT (as it bears on chunking and citation)

Measured on the current file (effective 2026-08-07):

```
Header      title + "These rules are effective as of <date>." + Introduction
Contents    plain-text TOC (sections and rule-groups, no page numbers)
Body        9 sections -> 147 rule-groups -> 1,172 rules -> 1,987 subrules,
            277 inline "Example:" lines
Glossary    739 terms
Credits     trailer
```

The grammar is fully regular; each unit is one physical line, with blank lines
between units:

- **Section**: `^([1-9])\. (.+)$` — e.g. `1. Game Concepts` … `9. Casual Variants`.
  (Appears twice: once in the TOC, once in the body — parse the body from the second
  occurrence of `1. Game Concepts`.)
- **Rule group**: `^(\d{3})\. (.+)$` — e.g. `702. Keyword Abilities`. First digit =
  section number.
- **Rule**: `^(\d{3})\.(\d+)\. ` — e.g. `100.1. These Magic rules apply …`. Note the
  trailing period after the number.
- **Subrule**: `^(\d{3})\.(\d+)([a-z]) ` — e.g. `702.19b The controller of an
  attacking creature with trample …`. No trailing period after the letter. Subrule
  letters **skip "l" and "o"** (stated in the CR introduction: 704.5k → 704.5m →
  704.5n → 704.5p), so letters are labels, not a dense sequence.
- **Example**: `^Example: ` lines directly follow the rule/subrule they illustrate and
  carry no number of their own — attach them to the preceding rule when chunking.
- **Glossary entry**: term on its own line (first non-blank line after a blank),
  definition paragraph(s) beneath, almost always ending with cross-references like
  `See rule 702.64, "Absorb."` Entries run `Abandon` … `Zone-Change Triggers`.

Chunking/citation consequences:

- **Rule number = citation ID.** Every normative statement has exactly one number
  (`601.2b`, `702.19`), which is what judges, rulings, and articles cite. Chunk at the
  rule level (`###.N` with its subrules and examples, splitting oversized rules at
  subrule boundaries) and store `rule_number`, `section`, `rule_group`, and
  `effective_date` as metadata on every chunk.
- **Rule numbers are mostly stable but not immortal.** New mechanics append to the end
  of 701/702 (e.g. 702.193, 702.194 added Jun 2026); renumbering does happen across
  editions (e.g. historic insertions shifting 723+ in section 7). Citations must
  therefore be pinned to (rule number, CR effective date) pairs; the Academy Ruins
  `/cr/trace/{rule}` endpoint tracks historical renumberings if cross-version mapping
  is ever needed.
- **Cross-references are abundant and regular** (`see rule 601.2b`, `See rule 702,
  "Keyword Abilities."`) — cheap to extract with one regex and useful as graph edges
  between chunks.
- Text uses typographic quotes/dashes and a UTF-8 BOM; normalize on ingest.

## 3. Mechanics/keyword documentation worth ingesting alongside the CR

### Inside the CR (primary, already covered by ingesting it)

- **Rule 701 — Keyword Actions** (scry, mill, goad, …), **rule 702 — Keyword
  Abilities** (deathtouch … through 702.194 teamwork; one numbered subsection per
  keyword, definitional and citable), **rule 703 — Turn-Based Actions**, **704 —
  State-Based Actions**.
- **The glossary (739 terms)**: every game term with a one-paragraph definition plus
  rule cross-references. Ideal as standalone chunks that link term → rule number.

The CR is the *only* official machine-ingestable mechanics reference. WotC's
"keyword glossary" pages under magic.wizards.com/how-to-play are marketing-style HTML,
unstable URLs (the previous glossary path now 404s), and add nothing the CR lacks.

### Supplementary machine-readable sources (verified live)

| Source | What it gives | Form |
| --- | --- | --- |
| Scryfall catalogs: `https://api.scryfall.com/catalog/keyword-abilities` (223 values), `/catalog/keyword-actions` (79), `/catalog/ability-words` (69) | Authoritative-in-practice enumeration of keyword names as they appear on cards; the same strings appear per-card in the `keywords` field of Scryfall card objects, linking mechanics ↔ cards | JSON, no auth |
| Scryfall bulk data `rulings` (`https://api.scryfall.com/bulk-data`, type `rulings`) | All official WotC (Gatherer) card rulings — per-card mechanics clarifications, updated daily | JSON bulk file |
| MTGJSON `https://mtgjson.com/api/v5/Keywords.json` | Same three keyword lists in one dated file (`meta.version` e.g. `5.3.0+20260823`) | JSON |
| Academy Ruins API `https://api.academyruins.com` (community-run; prerelease v0.7.0, endpoints may change) | CR parsed to JSON: `/cr/{rule}` rule lookup (works for subrules, e.g. `/cr/702.19b`), `/cr/glossary`, `/cr/toc`, `/cr/keywords`; historical raw CRs by set code (`/file/cr/{set_code}`); CR **diffs** between versions (`/diff/cr`); rule renumbering **trace** (`/cr/trace/{rule}`); also MTR/IPG text and diffs | JSON, no auth |

### Tournament-policy documents (adjacent, lower priority)

- **Magic Tournament Rules (MTR)**: PDF on the WPN site, e.g.
  `https://media.wizards.com/ContentResources/WPN/MTG_MTR_2026_Feb27_EN.pdf` (linked
  from `https://wpn.wizards.com/en/rules-documents`). Governs tournament procedure,
  not game rules. The IPG is no longer linked from that page; Academy Ruins archives
  both. Ingest only if we want to answer tournament-procedure questions.

## 4. Proposed ingestion approach (concise)

1. Scheduled fetcher scrapes `magic.wizards.com/en/rules` for the current `.txt` link
   (tolerant of filename drift), downloads, strips BOM/normalizes newlines, parses the
   `effective as of` date, and archives the raw file keyed by that date. Trigger on a
   schedule (weekly is plenty) and/or when a set releases.
2. Parser (the regexes in section 2) emits: rule chunks (rule + subrules + examples,
   with section/group/number/effective-date metadata), glossary chunks, and a
   cross-reference edge list.
3. Keyword enumeration from Scryfall catalogs joins mechanics names to both CR 701/702
   subsections and per-card `keywords` fields.
4. Keep each CR version forever; cite as `CR <rule number> (<effective date>)`.

## Sources

- CR landing page and file links: https://magic.wizards.com/en/rules (fetched 2026-08-23)
- CR text, structure, and self-description: `MagicCompRules 20260819.txt` at
  media.wizards.com (downloaded 2026-08-23; counts computed from the file)
- URL liveness checks: HTTP status probes of 2020/2023/2025/2026 dated files (2026-08-23)
- Update Bulletins: magic.wizards.com news, e.g.
  https://magic.wizards.com/en/news/announcements/marvel-super-heroes-update-bulletin,
  https://magic.wizards.com/en/news/announcements/the-hobbit-update-bulletin,
  https://magic.wizards.com/en/news/announcements/lorwyn-eclipsed-update-bulletin
- Scryfall: https://api.scryfall.com/catalog/keyword-abilities, `/keyword-actions`,
  `/ability-words`, https://api.scryfall.com/bulk-data (fetched 2026-08-23)
- MTGJSON: https://mtgjson.com/api/v5/Keywords.json (fetched 2026-08-23)
- Academy Ruins: https://api.academyruins.com/openapi.json and live endpoint probes
  (`/cr/702.19`, `/cr/702.19b`, `/link/cr`, `/cr/trace/702.19`) (2026-08-23)
- WPN rules documents: https://wpn.wizards.com/en/rules-documents (fetched 2026-08-23)
