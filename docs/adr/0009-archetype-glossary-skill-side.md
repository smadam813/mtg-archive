# Archetype vocabulary bridges on the skill side, not in card documents

Commander slang ("stax", "voltron", "aristocrats", "blink") has no token in card text and few Oracle Tags say it. A slang query embedded as typed lands on name-substring matches. ADR 0007 named the gap and said closing it is re-ranking or fixture work, not document composition. After the regrade, four queries in the archetype-vocab subset still scored zero: voltron, aristocrats, blink, and stax.

**The bridge is a curated glossary in the plugin's shared references** (`plugin/mtg-archive/references/archetype-glossary.md`). One row per term: a definition, the Oracle Tags to pull, the type-line words to pull, and the phrasing to send as the semantic query. The file states the rule and the eval applies it.

Pull one `POST /cards/search` page per mapped tag or type (60 cards, `edhrec_rank` order). Then send one `POST /cards/semantic-search` with the phrasing in place of the term. Merge with the pulled cards first.

The skill then re-ranks by reading and states the expansion as an assumption. The service never sees a slang term. This is the same seam GLOSSARY.md already gives the consuming agent (the re-ranking role, ADR 0006).

**Ingest keeps Scryfall's tag aliases for the `tags` filter only.** Each tag record's `aliases` land in a `tag_alias` table (alias, label), replaced per ingest run. The card query endpoints resolve an alias to its label in the same SQL statement as the other predicates. The map skips an alias that equals a label, so the label keeps matching as itself. A second claim on one alias is an ingest-report anomaly, and the first claim wins. Card documents never carry an alias, so an alias change queues no re-embed and leaves `embedding_state` as it is. Probed against the live Scryfall file of 2026-09-17 after ingest run 23: `tags: ["blink"]` matches Ephemerate, `["boardwipe"]` matches Wrath of God, `["sac outlet"]` matches Viscera Seer, and `["flicker"]` still matches Ephemerate.

**The result file is `eval/retrieval/results/2026-09-20-qwen3-embedding-0.6b.json`** (frozen tuple, no re-embed). The bare archetype-vocab subset scores nDCG@10 0.357 and recall@30 0.288. The glossary pass scores 0.671 and 0.511. Bare card search holds within run noise: 0.491 nDCG@10 and 0.425 recall@30, against 0.496 and 0.425 on 2026-09-08.

| query | nDCG@10 bare → glossary | recall@30 bare → glossary |
|---|---|---|
| voltron | 0.000 → 0.250 | 0.000 → 0.538 |
| aristocrats | 0.000 → 0.702 | 0.062 → 0.500 |
| blink | 0.000 → 0.675 | 0.000 → 0.750 |
| stax | 0.021 → 0.287 | 0.125 → 0.500 |
| tutor | 0.977 → 0.841 | 0.722 → 0.611 |
| group hug | 0.496 → 0.468 | 0.440 → 0.280 |

Tutor and group hug lose a little: their term is already a corpus tag word. The pull order is popularity within a tag, and the fixture expects staples. A broad root tag (`tutor`, `group hug`) pulls popular cards the fixture does not expect. The rows for those terms use narrower tags or none.

**Rejected.** A glossary document type in the card Search surface (option 2) changes document composition. The one experiment that added vocabulary text to documents (inherited tags) regressed card search, and each iteration costs a full re-embed. An external mapping import (option 3) does not reach the zeros. Scryfall's own aliases carry nothing for stax, voltron, or aristocrats, and EDHREC has no stable public API. Service-side query rewriting or a re-ranker is out: the service stays deterministic set arithmetic, and the skill chooses the words.

**Where the rule lives.** The pull size, the merge order, and the term match rule live in the glossary file's "Apply the glossary" section. The eval runner (`tests/MtgArchive.Tests/ArchetypeGlossary.cs`) applies the same rule to the same file. The eval reports the glossary pass as `card_search_archetype_vocab_expanded` beside the bare subsets. The bare keys keep their meaning, so ADR 0007's regression gate reads the same fields.

**Amendment (2026-10-04, [ADR 0012](0012-arena-brawl-formats.md)):** the pulls use the format's popularity order, not `edhrec_rank` order. For Commander the order is the same.
