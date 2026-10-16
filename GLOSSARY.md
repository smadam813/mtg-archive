# MTG Archive

A self-hosted MTG Commander, Arena Brawl, and Standard knowledge base: a corpus of cards and rules, an index over it, and a simulation engine. It lets an agent reason over a specific deck. This file is the glossary. Scope lives in the ADRs, and the done-queries live in `eval/acceptance/`.

## Language

**Corpus**:
The ingested primary sources — card data, Comprehensive Rules, mechanics documentation, and dated banlists. Card data is every Scryfall Oracle object with game meaning, tokens included; Art Series and Jumpstart front cards (`layout` `art_series` / `front_card`) are excluded at ingest because they only shadow real cards' names.
_Avoid_: database, dataset

**Index**:
The query surface over the corpus — structured filters plus semantic retrieval. Legality filters at its boundary.

**Search surface**:
One of the two semantic retrieval modes, each with its own fixed query instruction: card search (card documents, hard-filtered through `legal_candidates`) and rules search (CR rule and glossary chunks, no legality dimension). Filtered KNN in one SQL statement; no fusion, no re-ranker — the consuming agent re-ranks. A distance reproduces to about 0.003 across runs. Ollama's prompt cache shifts the query vector when the same query text embeds twice in a row. Two candidates within 0.003 of each other are a tie. The service trims the query and unifies CRLF to LF before embedding, so caller whitespace cannot move a distance.

**Card document**:
The per-face text embedded for card search: name, type line, oracle text verbatim (reminder text, mana and tap symbols included; ADR 0007), power/toughness or loyalty, plus appended direct Oracle Tags (inherited ancestors are filter-only). Faces dedupe to their card at query time; rulings are served with a card hit, never embedded.

**Oracle Tags**:
Scryfall's community-curated function labels ("ramp", "removal"). Double duty: a structured filter and appended text in card documents to bridge the archetype-vocabulary gap. A card's set is closed over Scryfall's tag ancestry: a tagging with a child ("tax attack") carries every ancestor ("pillowfort") as an inherited tag. Direct taggings carry Scryfall's weight; inherited ones carry none. Inherited tags serve the filter only; card documents embed direct tags.

Scryfall's tag aliases ("blink" for "flicker") live in their own table, replaced per ingest. The `tags` filter resolves an alias to its label before matching. Card documents never carry an alias, so no re-embed.

**Embedding state**:
The singleton record (model tag, dimensions, prefix-set version) the stored vectors were produced under. A mismatch with configuration disables semantic queries until re-embed completes — vector spaces never mix.

**Retrieval eval**:
The repo fixture of graded queries (65 card-search with expected `oracle_id` sets, 13 rules-search with expected rule numbers) scored nDCG@10 and recall@30 through the real service path, prefixes included. Gated the embedding-model choice (ADR 0007); now regression-only, re-run on any model-tag, prefix-set, or document-composition change.

**Ingest run**:
The single-flight, on-demand background job that refreshes the corpus: fetch and validate, card upsert, source-profile derive, card-feature derive, CR refresh, banlist replay, legality rebuild, popularity load, bracket load, re-embed. Phases commit as they land; derived tables go live only by an atomic swap after every upstream phase succeeded. Recovery from any failure is running it again.

**Ingest report**:
The run's outcome record: new and changed card counts, drift detections, CR version found, stale cards, collection rows pointing at stale cards, unmatched search-lands, unmatched enters-tapped lands, unmatched opponent or combat sources, unprofiled card faces, each Brawl format's legality counts and status changes, skipped popularity export rows, errors. Standard's counts and status changes sit beside the Brawl ones. Surfaced with the run, never silently dropped — the ingest-side sibling of the quarantine report.

**Drift**:
Commander only. A detected mismatch between a curated file and Scryfall's current state — a `legalities.commander` flip the banlist files don't explain (a commander-only or companion ban leaves the card legal in the deck, so Scryfall says `legal` for it), or a `game_changer` flip absent from the latest Game Changers snapshot. Report-only: the curated file stays the source of truth until a human appends the dated event with real provenance and re-ingests.

**Corpus state**:
The singleton recording the last successful ingest run per phase plus source freshness stamps (Scryfall `updated_at`, CR effective date and text hash, curated-file and popularity export hashes). An unchanged-source run after a successful run short-circuits to a no-op; a `force` flag overrides.

**Stale card**:
A card absent from the latest Oracle snapshot. Never deleted — excluded from legal candidates and search, retained so collection rows and ban history never dangle.

**Choice point**:
A decision the engine hands the pilot policy through the pilot view: the action to take in a main phase, the keep and the bottom of a mulligan, a cleanup discard, and a pick (a tutor, a scry or look, a library order, a name, a trigger order). In interactive disruption it adds a response: the action, or none, to take in a response window. Mana payment, targets, modes and X are the engine's.

**Pilot policy**:
The single interface that makes every choice at a choice point. Implementations are swappable; play skill lives here, never in the engine. The default play policy is the goal-aware greedy (`goal-aware-greedy`); the opt-in hold policy (`goal-aware-hold`) plays only with interactive disruption. The run's goal predicate is its objective. Its priority order is goal-satisfying actions, tutors that can find a goal piece, the land drop, ramp, draw, other castables. It is deterministic given state, does no lookahead, and never sees a disruption profile. The mulligan slot has two mulligan policies: the land window (`land-window`, the default) and the goal-aware (`goal-aware`, selectable per run).

In interactive disruption the pilot also answers in a response window, by the greedy response rule. It answers only when the disruption card threatens a goal card. It picks the cheapest response card whose profile proves the answer. It skips goal cards, "unless its controller pays" counters and pacts. The default policy's main-phase order does not change, so it may tap out or spend protection at sorcery speed. The hold policy is the opt-in answer to that.

**Hold policy** (`goal-aware-hold`):
The opt-in play policy for interactive disruption (ADR 0014). A request selects it with `play_policy`, and it needs `disruption_mode: interactive`. Both arms play it, and the pilot still never sees the disruption profile. It plays as the goal-aware greedy with two holds. The card hold never casts a response card whose only rank is "anything else". The mana reserve, while the whole goal holds, keeps untapped mana worth the cheapest response card in hand that answers removal or a wipe aimed at a goal card. When a disruption card hits, the reserve stops. The reserve counts mana value only, never colors. The floor label does not change.

**Pilot view**:
The engine-neutral game state that the pilot policy reads, and the only one. It lists the legal actions the engine offers, each with a stable id, the card, a kind and a cost, plus the card features of the cards in play. The pilot ranks the listed actions; the engine pays mana and chooses targets, modes and X. The view holds no engine-specific types. In a response window it also names the disruption card on the stack and the cards it aims at; the card comes from the disruption card table, never from the deck.

**Engine adapter**:
The translation between one simulation engine and the pilot view: today the XMage adapter (`Simulation/Xmage/`). Our goldfish engine's adapter went at cutover (ADR 0011). It reports card features only for effects its engine executes, so the pilot never ranks a card up for an effect that will not happen.

**Card feature profile**:
The features the pilot reads for one card: develops mana, draws, produced colors, tutor destination, and the cards a tutor finds. The adapter takes the static features from an Oracle-derived profile in the database, and takes the cards a tutor finds from XMage's own search. The `card_feature_derive` ingest phase derives that profile once per card face from closed template families and never guesses. A face is unprofiled when a sentence mentions draw, search or add outside the families. It keeps the features the templates proved. Engine coverage lists its card, and the ingest report names the sentence.

For interactive disruption the profile adds a response feature. For a counter it records the reach by types and colors, the "unless pays" amount and the game-loss clause. For protection it records each one-shot grant with its scope. A face is response-unprofiled when a sentence mentions an answer outside those families. This flag is separate from the unprofiled flag above.

**Goal predicate**:
The closed-vocabulary target a simulation run measures (e.g. `on_battlefield(card, by_turn)`, `cast(card, by_turn)`, composable with `all_of`). Doubles as the goal-aware pilot policy's objective. `on_battlefield` is point-in-time — the card is on the battlefield at the end of turn `by_turn`; `cast` counts only resolved casts, never countered ones. A leaf's `card` is the full decklist name, which matches the card on either face, or a playable face name, which matches that face only. A playable face is the front face of any multi-face card, or the back face of a modal double-faced card. Any other face name is an invalid goal.

**Result envelope**:
The tier-2 answer contract: a floor-labeled probability ("≥ X%") with its 95% confidence interval, run count, base seed, named play and mulligan policies, per-turn cumulative curve, replayable exemplar lines, the engine and its version, engine coverage, engine errors, and the decklist's deck verdict. The `simplifications` come from the format's game rules. Under a disruption profile it becomes the tier-3 contract: baseline and disrupted floors from paired same-seed runs, the resilience delta, both curves, per-event fired stats, and a tier-3 assumptions block. The envelope echoes the disruption mode. Under interactive disruption it adds per-event answered and not-cast rates, the deck's response cards, exemplar hits and answers, and an interactive assumptions block in place of the tier-3 one; the floor label does not change.

**Disruption profile**:
The optional schedule of disruption events on a simulate request (`[{ kind, turn }, ...]`). Deterministic — every run faces the identical schedule; its presence switches the run to paired tier-3 mode. Multiple events of one kind are allowed, each an independent one-shot. The pilot never sees it. When the user asks for resilience without specifics, skills send the named default profile — `counterspell` turn 3, `targeted_removal` turn 4, `board_wipe` only on request — and always echo the profile as an assumption. The disruption mode selects how the disrupted arm plays the profile: abstracted disruption (the default) or interactive disruption.

**Disruption event**:
One scheduled interaction from a closed vocabulary — `counterspell`, `targeted_removal`, `board_wipe`; unknown kinds are hard failures, growth on demand. One-shot: unlocks on its scheduled turn, fires by adversarial-toward-the-goal targeting (most expensive qualifying goal card, deterministic name tie-break), and resolves unconditionally — there is no response window. Never-fired events are reported as unused. In interactive disruption the scripted opponent casts the event's disruption card at the same fire point, by the same targeting, and skips a target it cannot legally target or cannot affect; fired then means the opponent cast the card. Each attempt has one outcome: **hit** (the card resolved), **answered** (a response card stopped it), or **not cast** (no target, so the event stays armed, or a lock stopped the cast).

**Disruption mode**:
The selector for how a run's disrupted arm plays its disruption profile: abstracted disruption (the default) or interactive disruption (ADR 0013). It needs a disruption profile, and the result envelope echoes it.
_Avoid_: opponent policy, per-event mode

**Abstracted disruption**:
Tier 3 (ADR 0005): each disruption event resolves unconditionally against an idle opponent, with no response window. The default disruption mode.

**Interactive disruption**:
The first tier-4 question (ADR 0013): a scripted opponent casts each disruption event as a disruption card with real mana, and our seat gets one response window per card. A duel in every format; the floor label stays.
_Avoid_: AI opponent, real disruption

**Scripted opponent**:
The idle opponent with one addition: it casts the disruption cards that the profile schedules, inside our seat's priority, and pays every cost, ward and tax while its card is on the stack. It has no lands, makes no choice of its own, and never answers a response.
_Avoid_: opponent policy, AI opponent

**Idle opponent**:
The opponent seat in a goldfish game: Isamaru, Hound of Konda and 99 Plains at the format's start life. It passes, keeps its hand, plays no land, blocks nothing and never loses. Every baseline arm and every tier-2 and tier-3 game plays against it.

**Disruption card**:
The real card that the scripted opponent casts for one disruption event kind, from a fixed service-owned table: Counterspell for `counterspell`, Atomize for `targeted_removal`, Rout for `board_wipe`. Its cost and colors decide which response cards work against it.

**Response window**:
Our seat's one chance to act while a disruption card is on top of the stack, in interactive disruption only. One answer deep: the opponent never answers our response.
_Avoid_: priority window

**Response card**:
A deck card whose response feature proves it can answer a disruption card: a counter whose reach covers the card, or a one-shot protection grant that covers the threatened card. The envelope lists the deck's response cards.

**Resilience delta**:
Baseline floor minus disrupted floor from paired same-seed runs, so the delta carries no sampling noise between arms. The number that separates fast from fast-and-resilient; resilience is rebuild speed (redundancy, recursion, commander recast through tax), never protection. Under interactive disruption the delta also counts the deck's response cards and the real rules of the disruption cards; the same request in both modes on one seed shows the difference.

**Blank card**:
The stand-in for a decklist card that XMage does not implement. It keeps the card's library slot and its decklist name, has no mana cost and no abilities, and is never cast. A goal that only blank cards answer is an invalid goal, and a commander XMage lacks makes the request fail.

**Card-correctness fixture**:
A test that one card behaves as its Oracle text says on XMage. It holds a start position, scripted actions and choices, and assertions. Each Oracle clause is asserted or marked not modeled. It runs through the sidecar's scenario mode. The fixtures began as the Oracle-clause checks of the retired staples, so a floor does not rest on an untested XMage card.

**Parity gate**:
The host check that decided when XMage may serve `/simulate` (ADR 0011). It played frozen parity decks on XMage and on our engine under the same pilot, seeds and goals, and passed a comparison when the 95% interval of the difference contained 0, confirmed on a fresh seed. It closed on 2026-09-30 (`eval/parity/cutover/`). The same tooling is now the engine regression gate: a candidate sidecar against the serving XMage on the same decks.

**Engine regression gate**:
The parity tooling after cutover. `parity-baseline` records the serving XMage on the frozen parity decks. `parity` plays a candidate sidecar on the same decks and compares it against that baseline with the same interval rule and the same `accepted.json`. `parity` refuses a baseline that another engine recorded, and `parity-baseline --only` does not merge into one. No tool can rebuild the parity decks.

**Filler card**:
A colorless card with a generic cost and no rules text but inert keywords, which stands in a parity deck for a nonland card our engine had no definition for when the decks were frozen. Both engines run it the same way, so a parity difference comes from the cards they both claim to run.

**Goldfish engine**:
XMage, patched for seeded determinism, playing a decklist against an idle opponent in a duel under the format's game rules. It runs tiers 2 to 4 (ADR 0011). One run is XMage's turn loop with the pilot's windows at the upkeep, the precombat main phase and the end step; every spell is cast at sorcery speed, and the envelope's `simplifications` say so. A run ends at the turn horizon, on a win, or when life reaches 0. The mulligan gives the format's free mulligans (CR 103.5d). A multi-face card plays as XMage plays it; the pilot view names each face. The library starts from a keyed start order (a key over the run seed, the `oracle_id` and the copy index), so entry order has no effect. In interactive disruption the disrupted arm seats a scripted opponent in place of the idle opponent and opens a response window when a disruption card is on the stack.

**Game rules**:
The per-format values a simulated game plays under: start life, commander damage, free mulligans, and the turn-1 draw. Commander: 40 life, commander damage, one free mulligan, a turn-1 draw (CR 103.8c). The Brawl formats: 25 life, no commander damage, on the play with no turn-1 draw (CR 103.8a), one free mulligan, or none in Competitive Brawl. The format fixes them; a request never sets them. Tier 1 and tier 2 read the same record.
_Avoid_: game type, match options

**MDFC land**:
A modal double-faced card with a nonland front face and a land back face (`Shatterskull Smashing // Shatterskull, the Hammer Pass`). A pathway has a land on both faces, so it is not an MDFC land.
_Avoid_: modal land, spell-land

**Turn horizon**:
The turn cap on every run: the run ends after that turn's cleanup. Default 10, configurable per request.

**Simulation tiers**:
Tier 1 closed-form draw math, tier 2 Monte Carlo on XMage against an idle opponent, tier 3 abstracted disruption, tier 4 full rules engine. XMage runs tiers 2 to 4 (ADR 0011). The first tier-4 question is interactive disruption (ADR 0013).

**Decklist**:
The shared deck input contract: optional explicit commander(s), an optional companion, plus entries of card (exact name or `oracle_id`) and quantity, names resolved service-side like collection import. A name that does not resolve and starts with `A-` resolves to the original card, beside the front-face fallback. Partial lists are first-class input: tier 1 accepts them and attaches no verdict; the deck verdict calls them incomplete.

**Arena export**:
The text MTG Arena writes for a deck: `Commander`, `Companion`, `Deck`, and `Sideboard` sections of `<qty> <name> (<SET>) <number>` lines. The service parses it, or a plain `<qty> <name>` list, into a Decklist. It carries no format line.

**Tier-1 report**:
The tier-1 answer contract: exact closed-form results — category draw probabilities, the mana-source table, pip demand vs. supply per color and turn, the land-drop line — plus mulligan looks (L, from the format's free mulligans), an input echo and an assumptions block (the format's turn-1 draw rule, no mulligans, cast at mana value, land drops assumed, ETB-tapped ignored, Pathways count for both colors, Phyrexian pips count zero, opponent-dependent sources excluded, token for another player excluded). The assumptions block stands where tier 2's floor label stands; no CI, no seed, no floor. Raw probabilities only — consistency targets are skill-side policy.

**Category draw probability**:
P(≥ k cards of a caller-supplied card set among the cards seen by turn N), where cards seen = 7 + N with a turn-1 draw and 7 + max(0, N−1) on the play, from the format's game rules, matching the engine. Subsumes single-card draw odds; the set carries no semantics — the caller defines it.

**Mana source**:
A card that can provide mana: non-empty `produced_mana`, or a fetch resolved deck-relatively (the union of colors of deck lands matching its fetch criteria). Counts fully toward every color it can produce; the source table also reports each source's color spread (mono / dual / 3+), so shared sources are visible. An identity-bound source makes only the colors of the deck's color identity, and its spread counts only those colors. The deck's color identity is the union of the commanders' color identities. A deck with no commander uses the union of every card's color identity. The per-color counts cover the deck's color identity and colorless only. Tier 1 excludes an opponent-dependent source and a token for another player, and lists each in the report as excluded.

**Source profile**:
The ingest-derived record for every land and every mana producer: its produced colors, the land types it can fetch, parsed from the closed "search your library for a … land" template family, and for a land its enters-tapped state and its fetch activation. The record also says whether the source is identity-bound, opponent-dependent, or a token for another player. An unmatched search-land provides nothing and lands in the ingest report. Fetch profiles resolve to colors per deck at query time.

**Identity-bound source**:
A source-profile facet for a card that produces all five colors. Every "Add … any color" ability binds those colors to the commander's color identity or to lands the players control. Examples: Command Tower, Arcane Signet, Exotic Orchard, Fellwar Stone, Reflecting Pool. One unbound any-color ability (Plaza of Heroes) leaves the card unbound. Tier 1 clips the source to the deck's color identity and does not model the opponents' lands.

**Opponent-dependent source**:
A source-profile facet for a nonland card whose every mana-producing ability and every fetch ability depends on another player or on combat: a trigger on an opponent's action, a trigger on combat damage, an amount that counts what opponents control, or a condition that an opponent controls more than you. Examples: Smothering Tithe, Revel in Riches, Dockside Extortionist, Carpet of Flowers, Land Tax, Knight of the White Orchid. One ability that does not depend on them (Coveted Jewel's {T} ability) leaves the card a source. An attack trigger alone does not make an ability dependent. One-shot producers (Dark Ritual, Lotus Petal, Simian Spirit Guide) stay sources. Tier 1 has no opponents, so it excludes the card and reports it as excluded. Tier 2 plays the card's own text.

**Token for another player**:
A source-profile facet for a nonland card whose every source ability creates its mana token for another player. The ability says "its controller creates" after a counter or a removal, or "target opponent creates", or it is a Gift keyword. Examples: An Offer You Can't Refuse, Buy Your Silence, Wanted Scoundrels, Blooming Blast. A token you get ("create", "you create", "each player creates") keeps the card a source (Deadly Dispute, Marching Duodrone). Tier 1 excludes the card and reports it with reason `token_to_another_player`. Tier 2 plays the card's own text.

**Enters-tapped state**:
The source-profile facet that says whether a land enters tapped: `untapped`, `tapped`, or `conditional`. The parser reads the land faces sentence by sentence against the closed "enters tapped" template family. It first removes reminder text and quoted granted-ability text. A free player choice is `untapped` (shock lands: "you may pay N life. If you don't, it enters tapped"; "you may have this land enter tapped"). A choice gated on game state is `conditional` ("you may reveal a Giant card from your hand. If you don't, …"), and so is any "unless" or "if" clause. A declined choice ("If you don't, …") reads the sentence before it. When that sentence offers no "you may" choice, the land is unmatched. A land face with no "enters tapped" sentence is `untapped`: "enters untapped" is not "enters tapped". An unmatched sentence, or two land faces that disagree, leaves the land with no facet and an ingest-report entry. Search-only: the availability turn does not consume it.
_Avoid_: ETB state, land entry

**Fetch activation**:
The source-profile facet for a land whose fetch is the closed "{T}, [Pay N life,] Sacrifice this land: Search your library for …, put it onto the battlefield [tapped], then shuffle" template: `untapped` or `tapped`, the state the found land enters in. Any other fetch shape (a mana cost, a trigger, a search to hand, a spell) has no facet and no report entry: the fetch types still stand. Tier 1 and the pilot read it; XMage plays the land's own text.

**Availability turn**:
The earliest turn a mana source counts as producing: lands on their drop turn (ETB-tapped ignored), nonland producers at mana value + 1. Supply at turn T counts only sources with availability ≤ T.

**Pip demand**:
A spell's per-color colored-pip requirement, demanded on the turn equal to its mana value. Hybrid pips form an "either X/Y" demand line, Phyrexian pips count zero, X adds nothing beyond printed pips. The commander's demand is included, flagged as commander. Its per-color casting probability is the category draw probability of the color's turn-available source set.

**Format**:
The rules set a deck is built and judged under: Commander, Brawl, Competitive Brawl, Standard Brawl, or Standard (ADR 0012). Each format has its own card pool, bans, deck shape, and game rules. Each format with a commander seat has its own commander rule. Standard is the 60-card constructed format, not Standard Brawl. It serves card legality only so far. A request that names no format means Commander.
_Avoid_: queue, variant, Historic Brawl

**Brawl formats**:
Brawl, Competitive Brawl, and Standard Brawl together: the three MTG Arena Brawl formats. "Brawl" alone means the 100-card Arena format, formerly Historic Brawl.

**Role**:
The seat a card occupies in a deck: commander, companion, or main deck. The main deck is the 99 in Commander, Brawl, and Competitive Brawl, and the 59 in Standard Brawl. Standard has no commander or companion seat, so every card sits in the main deck. Its sideboard is not a role yet. Legality is judged per role.

**Legality**:
A deterministic, structural attribute per (card, format, role), derived once at ingest. Commander adds the banlist version. Never a boolean per card, never a model judgment. One of: legal, banned, not legal (outside the format's card pool), or not eligible (fails the role's requirements in that format). A Commander ban cites a dated ban event; a Brawl or Standard ban cites the Scryfall key and the legality as-of date.

**Legality as-of**:
The Scryfall bulk `updated_at` that dates the legality of a Brawl format or Standard. That legality is current state only, so the as-of date cannot be pinned.

**Banlist version**:
Commander only. A dated snapshot of the Commander banlist, produced by replaying ban events. A decklist is legal only relative to one. A pinned historical version means today's card pool under that date's ban state.

**Ban event**:
Commander only. One dated change to the banlist: card, action, scope (all roles, commander-only, or companion), and source announcement. The unit of banlist history and the citation target for a banned status. A ban event expanded from a category ban also names its category.

**Banlist events file**:
The git-versioned curated transcription of Commander banlist history (`data/banlist-events.json`) — the source of truth the ingest replays into banlist versions and ban events. Cards referenced by exact Oracle name; an unresolvable name is a hard ingest failure. It records only the cards the official list named one by one, dated when the list first named them. Category bans stay out of it; the banlist categories file holds them. Wayback-transcribed full-list fixtures property-test the replay of this file alone.

**Category ban**:
A line on the official list that bans a group of cards without naming them: ante cards, Conspiracy cards, and the cards Wizards removed in 2020 for racist depictions. The git-versioned curated banlist categories file (`data/banlist-categories.json`) records each one with the list's wording, the dated announcement that put the ban in force, and one member rule from a closed vocabulary: a card type, a whole word in the Oracle text, or exact Oracle names. The replay expands each category into one `ban` event per member card, scope all, at that date (ADR 0010). A category that matches no card, or a date that another announcement already holds, is a hard ingest failure. Silver-bordered cards need no category: Scryfall calls them `not_legal`, so legality already says not legal.
_Avoid_: format rule, blanket ban

**Copy limit**:
A card's per-deck allowance, derived at ingest: 1 (singleton) by default; unlimited for basic lands and "any number" cards; 7 for Seven Dwarves. The limit is per format: Standard allows four copies. Only the singleton limits are derived so far.

**Deck shape**:
A format's deck-level rules: deck size (100, or 60 in Standard Brawl), copy limit, color identity, commander pairing, and the companion slot. Standard's shape is a 60-card minimum, a 15-card sideboard, and four copies. It is not modeled yet.
_Avoid_: deck legality, deck validity

**Deck verdict**:
The service's deterministic judgment of one decklist in one format. It combines card Legality and Deck shape. One of: legal; incomplete (more cards can fix every finding); illegal; unknown (legality rows are missing). Never a model judgment.

**Printing**:
A specific edition of a card. The corpus is keyed by functional card (`oracle_id`), and the collection resolves to `oracle_id` by name, keeping raw import rows so printing data survives. Printings stay deferred.

**Bracket**:
Commander only: a Brawl format has no power target (ADR 0012). The Commander power-level classification ("competition-ready" resolves to one), per the WotC beta spec and Game Changers list. Heuristic, never deterministic, no shared code path with legality. A classification reports a bracket floor plus declared assumptions, never a single unqualified bracket. The target bracket is a caller parameter; mapping phrases like "competition-ready" to a bracket is the skill's job, not the service's.
_Avoid_: power level

**Bracket floor**:
The minimum bracket provable from decklist signals alone; the primary value of a bracket classification. Caller-declared assumptions can raise the assumed bracket, never the floor.

**Barometer**:
One spec-defined signal the bracket classifier reads from a decklist: Game Changers count, mass land denial, chained extra turns. Definitions and per-bracket thresholds live in versioned spec data, not code.

**Game Changers list**:
WotC's periodically revised card list whose per-decklist count is the bracket spec's only numeric rule. Versioned as dated snapshots keyed by announcement date.

**Collection**:
The single user's owned cards with per-card quantities, keyed by `oracle_id` — a first-class constraint equal in standing to format legality. Captured by full-replace import from a ManaBox export or a plain-text list. One collection serves every format.

**Collection scope**:
A caller-chosen index query mode that hard-filters candidates to owned cards at the boundary, the same pattern as legality. When scoped, a card's availability is min(copy limit, owned quantity), reported as `available` on the candidate. The caller sets it; the service never infers it.

**Quarantine report**:
The import's list of rows that failed to resolve to a corpus card (misscans, tokens, name drift). Returned with the import result — never silently dropped, never fatal.

**Recommendation loop**:
The skill-side optimization loop behind build-from-collection: the agent generates candidates from the index under collection scope, assembles decklists with its own judgment, gates each through the cost ladder, spends tier-2 simulate calls as the fitness function, swaps, repeats. Commander is the outer choice, the main deck the inner loop; the commander is revisited only when the inner loop plateaus below target. Lives in the skill — the service exposes deterministic primitives only.

**Iteration budget**:
The declared skill-side cap on tier-2 simulate calls per recommendation request (default 15; the caller can raise it explicitly), stated in the answer. Never service-enforced; no mid-loop check-ins.

**Cost ladder**:
The recommendation loop's spend order: the tier-1 screen (pip supply vs demand, category draw odds) and, in Commander, bracket classification gate every candidate. These gates are cheap. Only a deck that passes every gate spends a tier-2 simulate call. Tier 1 suffices for mana and card-category questions; tier 2 is required once the goal names card semantics or sequencing. Doubles as `deck-check`'s tier-selection order for read-only questions: the cheapest tier that answers. `build-deck` owns every swap, mana swaps included; `deck-check` stays read-only and routes change requests to `build-deck`. Interactive disruption is the top rung, above abstracted disruption: it costs more per pair and answers whether the deck's own answers save the goal. Build-deck does not use it.

**Deck tune**:
The `build-deck` entry that starts from a decklist the user supplies instead of assembling one. It skips commander choice and assembly, runs the recommendation loop from the tier-1 screen, and never revisits the commander. Swap-ins are collection-scoped; the skill does not check that the user owns the supplied cards.

**Mana goal**:
A goal stated as casting a color's heavy-pip spells on time. A color's heavy-pip rows are its 3+-pip rows, or its 2+-pip rows when it has none. Tier 1 answers a mana goal alone: fitness is the lowest casting probability over the goal rows (the named color's heavy-pip rows, at mana-value turn). A candidate that drops another color's heavy-pip row below the baseline fails the screen. A mana goal spends no simulate call.

**Stopping criteria**:
The recommendation loop stops on the first of: target met (95% CI lower bound of the goal floor ≥ the skill's declared target), plateau (three swap rounds without CI-lower-bound improvement), or iteration budget exhausted — the latter two report best-so-far with the gap stated. A fourth criterion, target out of reach, is a declaration, not a stop. The skill makes it before the first simulate call when the reach bound is below the target. The loop then runs to plateau or budget exhausted and never declares success.

**Reach bound**:
The skill-side upper bound on the goal probability of a decklist: `1 - (1 - q)^L`. The route category holds every card that can move the goal card out of the library or change the library order, each counted as if it always succeeds. `q` is the tier-1 probability that the route category is seen by the goal turn. `L` is the most seven-card hands the mulligan policy can look at, from the tier-1 report: 4 with one free mulligan, 3 with none. In Commander, the bound counts every owned route card the target bracket allows. In a Brawl format, it counts every owned route card legal in the format, with no cap. It then holds for every decklist the collection can build for one commander under that limit. The tier-1 probability alone is not a bound, because the engine mulligans. A goal piece in the command zone has no reach bound.

**Confirmation run**:
The fresh-seed tier-2 run that validates a target-meeting candidate before the loop declares success. Candidates are otherwise compared under one fixed base seed. Under that seed, the cards two candidates share keep their relative opening library order. A later shuffle runs over a library that differs, so it matches only in part. The confirmation run guards against overfitting to those shuffles and counts against the iteration budget.

**Commander-alternatives query**:
The dedicated deterministic service query behind done-query 1: candidates = commander-role-legal cards in the request's format (collection scope caller-set), ranked by retained count of the existing main deck. "Preserve color identity" is the retention metric itself — a card is lost iff its color identity escapes the candidate's, or it is banned or not legal in the format, each loss citing its reason. Ties break by color-identity closeness to the original commander, then popularity order. The companion is judged by the same rules but not counted. Function-similarity re-ranking is agent-side.

**Retention tier**:
The candidates of a commander-alternatives query that share one retained count and one color-identity distance. The agent re-ranks within a tier by semantic distance to the old commander's Oracle text, and never across tiers. Semantic distance takes the place of popularity order as the tie-break; popularity order only breaks distance ties.

**Popularity order**:
The per-format order of candidates, from most played to least. Commander, Standard Brawl, and Standard use EDHREC rank. Brawl and Competitive Brawl use the popularity export first, then EDHREC rank. Cards with no rank sort last, by name, then by oracle id. A commander search orders by the share of games with the card as commander first, where the source has one. A card's popularity rank is its 1-based position in that order. An answer states the source and its as-of date.
_Avoid_: EDHREC order (for a Brawl format)

**Popularity export**:
The maintainer's manual Untapped.gg card-data file for Brawl or Competitive Brawl and one date window, kept on disk under `data/popularity/`; git does not hold it. Standard Brawl has no export. The as-of date is the window's end.
_Avoid_: Untapped scrape, popularity feed

**Engine coverage**:
The result envelope's report of the cards a floor does not fully cover, in two lists. Unimplemented cards are the cards the engine cannot run: XMage plays each as a blank card. Unprofiled cards run in full, but their card feature profile cannot prove every feature, so the pilot reads fewer features than the card has. Engine coverage replaced DSL coverage at cutover (ADR 0006, ADR 0011). Under interactive disruption a third list names the unprofiled response cards, so the answered rate is a lower bound.

**Citation**:
The structured reference every computed answer carries: `{ type, id, label }` with type one of card, rule, ban_event, legality, or snapshot. A `legality` citation is one card's legality status in a Brawl format or Standard. Its id resolves through card detail, whose row holds the Scryfall URL, the key, and the as-of date. Every id is fetchable from a reference endpoint — no prose-only citations.

**Reference endpoint**:
A GET endpoint serving a citation target verbatim: card detail, CR rule or glossary entry, ban event, banlist version, corpus state. Citations are only useful to an agent if every one is fetchable.

**Versions block**:
The echo on every computed response of the versioned inputs actually used — format, banlist version or legality as-of date, popularity source and its as-of date, bracket spec version, Game Changers snapshot, CR effective date. Version parameters default to latest; pinning an unknown version is a request error, never a silent fallback.

**Done-queries**:
The three end-to-end queries in the vision doc that define MVP completion and set the capability bar. The Brawl acceptance queries (BQ1 to BQ3) hold the Brawl formats to the same bar.

**Skills surface**:
The five Claude Code skills — `mtg-lookup`, `deck-check`, `commander-swap`, `build-deck`, `archive-admin` — shipped together as the `mtg-archive` plugin hosted in this repo; the service's only consumer. Skills reach the service at a fixed localhost URL, overridable via `MTG_ARCHIVE_URL`; a down service means "start the compose stack", never a recall answer. `mtg-lookup` triggers aggressively: any MTG fact question routes through the archive — cite it or say the archive lacks it.

**Archetype glossary**:
The skill-side table (shared plugin reference; the service never sees slang) for Commander slang ("stax", "voltron", "aristocrats"). Each row maps a term to a definition, the Oracle Tags and type-line words to pull, and the phrasing to send as the semantic query. A skill that searches from the user's wording applies it and states the expansion as an assumption. The Retrieval eval scores the archetype-vocab subset once bare and once through this file, the glossary pass (ADR 0009).

**Answer contract**:
The shared reporting convention every skill follows, held in one plugin reference file: citations rendered as fetchable footnotes, the versions block echoed once, and assumptions stated where they bite — the phrase-to-format mapping used, floor labels, tier-1 assumptions, the phrase-to-bracket mapping used, the disruption profile assumed. Under interactive disruption it also states the disruption mode, the scripted opponent and the interactive assumptions.

**Phrase-to-format mapping**:
The skill-side table (shared reference; the service never sees phrases) that turns the user's wording into a format. No format word means Commander. Ambiguous wording ("Arena" alone, or an Arena export with no format word) means ask the user. The chosen mapping is always declared in the answer.

**Phrase-to-bracket mapping**:
Commander only. The skill-side table (shared reference; the service never sees phrases) resolving power-level phrasing to a target bracket: precon/jank/theme → 2, casual → 2, upgraded/mid/focused → 3, competition-ready/high-power/optimized → 4, cEDH → 5. Unmatched phrasing → ask the user; the chosen mapping is always declared in the answer.
