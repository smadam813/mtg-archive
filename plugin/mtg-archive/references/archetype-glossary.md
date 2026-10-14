# Archetype glossary

Commander players name strategies with slang: "stax", "voltron", "aristocrats". Card text never says these words, and few Oracle Tags do. A slang query embedded as typed lands on name matches, not on function. This file bridges the gap on the skill side. The service never sees a slang term; the skill chooses the words.

This file is the one source of truth. The retrieval eval reads this table too. Do not copy it.

## Apply the glossary

Apply it before every card search that starts from the user's wording.

1. Find each glossary term the user's wording contains. Match whole words, case-insensitive, plural allowed ("board wipes"). Prefer the longest term ("card draw engine" over "draw").
2. Pull by structure first. For each mapped tag, call `POST /cards/search` with `tags` set to that one tag and `limit` 60. For each mapped type, call it with `types` set to that one type. Both filters are conjunctive, so send one value per call. The page comes back in the format's popularity order. Send the same filters (`format`, `color_identity`, `role`, `collection_scope`, ...) on every call.
3. Then search by function. Call `POST /cards/semantic-search` with `k` 60. In the `query`, replace the term with its phrasing and keep the rest of the user's wording.
4. Merge the hits. Pulled cards come first, in the format's popularity order across the pulls (lowest `popularity_rank` first). Semantic hits follow, closest first. Drop a card that already appeared.
5. Re-rank by reading, as the skill says. The merge is a starting order, not a verdict.
6. State the expansion as an assumption in the answer: the term, the tags and types you pulled, and the phrasing you sent. See the assumption rule in `answer-contract.md`.

A tag in this table is a label or an alias from Scryfall's tag file. The `tags` filter accepts both. The retrieval eval applies the pull rule and the merge rule from this section. Keep the numbers here current when you change the rule.

## Terms

The columns are the term as players say it, a one-line definition, the Oracle Tags to pull, the type-line words to pull, and the phrasing to send as the semantic query. A blank tags or types cell means no pull.

| term | definition | tags | types | phrasing |
|---|---|---|---|---|
| ramp | Cards that add mana or lands faster than one land drop per turn. | ramp |  | Search your library for a basic land card and put it onto the battlefield. Add mana. Extra lands and mana rocks that accelerate your mana. |
| removal | Cards that answer one permanent or creature an opponent controls. | removal |  | Destroy target creature. Exile target permanent. Destroy target artifact or enchantment. Spot removal that answers one threat. |
| stax | Cards that tax, lock, or shrink every player's resources so nobody can act freely. | stasis, cost increaser, rule of law |  | Spells cost {1} more to cast. Permanents don't untap during their controllers' untap steps. Each player can't cast more than one spell each turn. Each player sacrifices a permanent. |
| board wipe | Cards that destroy, exile, or bounce every creature or every nonland permanent at once. | sweeper |  | Destroy all creatures. Exile all nonland permanents. Each creature gets -X/-X until end of turn. Mass removal that clears the whole board. |
| card draw engine | Permanents that draw extra cards every turn, not once. | draw engine |  | Whenever a creature enters the battlefield under your control, draw a card. At the beginning of your upkeep, draw a card. A permanent that draws cards again and again each turn. |
| tutor | Cards that search your library for a specific card. | tutor-card, tutor-to-top |  | Search your library for a card, put it into your hand, then shuffle. Search your library for a card and put it on top of your library. |
| reanimator | Cards that fill a graveyard with creatures and return them to the battlefield cheaply. | reanimate-creature, mass reanimation |  | Put target creature card from a graveyard onto the battlefield under your control. Return target creature card from your graveyard to the battlefield. Search your library for a creature card and put it into your graveyard. |
| counterspell | Cards that counter a spell on the stack. | counterspell |  | Counter target spell. Counter target noncreature spell unless its controller pays mana. Counter target spell if it was cast this turn. |
| aristocrats | Cards that sacrifice your own creatures for value and drain opponents when creatures die. | blood artist ability, free sacrifice outlet |  | Sacrifice a creature: scry 1. Whenever a creature dies, each opponent loses 1 life and you gain 1 life. Whenever you sacrifice a creature, each opponent sacrifices a creature. |
| pillowfort | Cards that stop or tax creatures attacking you. | pillowfort |  | Creatures can't attack you unless their controller pays {2} for each creature. Creatures can't attack you. Prevent all combat damage that would be dealt to you. |
| voltron | Cards that load one creature, usually the commander, with Auras and Equipment and win with combat damage from that creature (in Commander, commander damage). |  | Equipment, Aura | Equipment. Equipped creature gets +3/+3 and has hexproof and haste. Equip {2}. Aura. Enchant creature. Enchanted creature gets +2/+2 and has trample. |
| wheel | Cards that make every player discard their hand and draw a new one. | wheel |  | Each player discards their hand, then draws seven cards. Each player shuffles their hand and graveyard into their library, then draws seven cards. |
| mana rock | Noncreature artifacts that tap for mana. | mana rock |  | Artifact. {T}: Add {C}. {T}: Add one mana of any color. A noncreature artifact that adds mana. |
| blink | Cards that exile a permanent and return it to the battlefield to reuse its enters trigger. | flicker |  | Exile target creature you control, then return that card to the battlefield under its owner's control. Exile a permanent, then return it to the battlefield at the beginning of the next end step. Flicker a creature to trigger it again. |
| group hug | Cards that give every player cards, mana, or lands, so the table speeds up together. |  |  | group hug: each player draws an additional card at the beginning of their draw step. Each player may put a land card from their hand onto the battlefield. |
