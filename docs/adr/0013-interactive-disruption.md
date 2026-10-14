# Interactive disruption: the first tier-4 question

Tier 3 (ADR 0005) resolves each disruption event with no response window. The deck's own counterspells and protection count for nothing, and so do hexproof, indestructible and "can't be countered". A deck with five counterspells and a deck with none get the same disrupted floor.

ADR 0011 put tier 4 in scope and asks each tier-4 question for its own throughput budget. **Interactive disruption is the first tier-4 question: an opt-in disruption mode beside abstracted disruption.** A scripted opponent casts each scheduled event as a real XMage card with real mana, and our seat gets one response window per card. Abstracted disruption stays the default, as ADR 0005 designed it, and tier 2, tier 3 and every baseline arm do not change.

## The request and the arms

- `POST /simulate` takes one optional top-level member, `disruption_mode`: `abstracted` or `interactive`. A request with no member runs abstracted disruption. A mode with no `disruption_profile` is a 400 `invalid-disruption-mode`, and so is an unknown value. The profile keeps its `[{kind, turn}]` shape.
- A run keeps two arms on the same seeds. The baseline arm is the tier-2 game against the idle opponent in both modes. The disrupted arm is abstracted or interactive. The resilience delta stays baseline minus disrupted.
- `runs` counts pairs, so the caller still pays twice as many games.
- To see what the deck's answers add, a skill sends the same request and seed once per mode. The baseline arms are then byte-equal.
- All four formats allow interactive disruption, because it is a duel.

## The scripted opponent

- The scripted opponent is the idle opponent with one addition. It keeps the idle opponent's name, its deck (Isamaru, Hound of Konda and 99 Plains) and its start life. It passes, keeps its hand, plays no land, blocks nothing and never loses.
- It acts only through the disruption profile. It does nothing else, so the two arms stay identical up to the first disruption card.
- It casts inside our seat's priority, at the tier-3 fire points. A counterspell goes right after our cast. Targeted removal and a board wipe go at our end step, after the end-of-turn reply. XMage then gives our seat priority with the card on the stack.
- A failed cast leaves no disruption card in any zone. The opponent refuses any other cast of a disruption card, for example a recast that Delay offers.

## The disruption card table

The service owns one fixed, engine-neutral table. The owner approved it.

| Event kind | Disruption card | Cost and type | Effect |
|---|---|---|---|
| `counterspell` | Counterspell | {U}{U}, instant | Counter target spell. |
| `targeted_removal` | Atomize | {2}{B}{G}, instant | Destroy target nonland permanent, then proliferate. |
| `board_wipe` | Rout | {3}{W}{W}, sorcery | Destroy all creatures. Cast with its "pay {2} more" flash ability. |

The colors decide which color-specific answers work. For example, Pyroblast works only against Counterspell, and Veil of Summer only against Atomize.

**The mana rule.** The opponent has no lands. While its disruption card is cast or is on the stack, it adds exactly the unpaid cost and pays it. In that window it also pays every ward cost and every "unless its controller pays" tax. So soft counters such as Mana Leak never stop a disruption card. Outside the window it behaves as the idle opponent. The owner approved this rule as the adversarial default.

## Targets and responses

- **Legal and affectable.** The service proposes an ordered candidate list by the tier-3 adversarial rule. Removal extends it: goal-relevant nonland permanents by mana value descending, then by name, then every other nonland permanent in the same order. The opponent takes the first candidate that the card can legally target and can affect. Atomize skips an indestructible permanent, and Counterspell skips a spell that "can't be countered". Ward is not a reason to skip, because the opponent pays it. With no candidate the card is not cast, and the event stays armed for its next fire point.
- **One answer deep.** Our seat gets one response window per disruption card. The opponent never answers our response, and at most one disruption card is on the stack.
- **The greedy response rule.** The goal-aware greedy pilot answers only when the disruption card threatens a goal card. It picks only an answer whose card feature profile proves it works against that card. It skips goal cards, "unless its controller pays" counters and pacts, and then takes the lowest mana value. The pilot does no lookahead.
- **No hold rule in v1.** The main phase plays as it does today, so the pilot may tap out or spend protection at sorcery speed. The interactive assumptions block declares this.
- Each event has one outcome per run: hit, answered or not cast.

## Floors and attribution

- Every arm keeps the floor label "≥ X%, greedy policy" and the policies `{play: goal-aware-greedy, mulligan}`.
- No opponent policy name exists. The opponent makes no choice of its own, so ADR 0005's rejection of an opponent-policy seam stands.
- Every open choice goes toward a lower floor: the opponent pays every tax, targets must be legal and affectable, answers are goal-only, and the main phase can tap out. Three choices can raise the floor: one answer deep, an opponent that acts only through the profile, and an opponent that acts only on our turn. The interactive assumptions block declares each one.
- The envelope echoes the mode and adds per-event answered and not-cast rates, the deck's response cards and an interactive assumptions block. Engine coverage lists the response cards that the archive cannot profile, so the answered rate is a lower bound.

## The throughput budget

One interactive request at the mode's run cap must finish in under 120 s on this host, with at most 1% engine errors. The reference is the done-query 2 final 99, the goal `on_battlefield` Pithing Needle by turn 4, and the default profile. If the request passes at 10,000 pairs, the interactive cap stays 10,000 pairs. Otherwise the interactive cap is 5,000 pairs, and the throughput record and this ADR record it. A later shim change that adds round trips re-runs the measurement.

## Considered options

- **An XMage AI seat.** Rejected: the floor would measure XMage's AI, not the deck. ADR 0011 requires the goal-aware pilot to make the choices.
- **Opponent lands.** Rejected: a land drop and a mana base make the opponent miss casts for reasons outside the deck, and the arms stop being identical before the first disruption card.
- **A pre-filled spare-mana pool.** Rejected: spare mana in the pool changes what the opponent can do outside its card. The pay-on-demand window pays exactly the cost and every tax.
- **A third arm.** Rejected: two runs, one per mode on the same seed, give the same comparison with no new member.
- **A cast in the opponent's own priority.** Rejected: the opponent would need turns, phases and a priority policy of its own. A cast inside our seat's priority keeps the tier-3 fire points.
- **An opponent-policy seam.** Rejected for the reasons in ADR 0005. A swappable opponent moves the result toward the opponent and away from the deck.
- **A shim-side schedule.** Rejected: the schedule, the event vocabulary and the targeting rule stay in .NET. The shim names cards and ids only.
- **A counter war.** Rejected: one answer deep keeps each event one card deep, as the profile declares.
- **A hold rule in v1.** The owner deferred it to a follow-up. The interactive assumptions block declares the tap-out limit.
- **A per-event mode key.** Rejected: one top-level member keeps the profile's pinned shape, and a mixed run has no clean floor.
- **A configurable card table.** Rejected: a per-request or per-format table makes floors incomparable across requests. The vocabulary stays at three kinds.

## Consequences

- Interactive can differ from abstracted even with no answers. Rout destroys tokens, locks stop casts, and the skips change targets. A one-time calibration on a deck with no response cards bounds this.
- The wire protocol moves to version 2. A stale sidecar image fails at hello.
- The card feature profile gains a response feature, and a forced ingest follows its merge.
- A shim change that touches the interactive path re-runs the engine regression gate with exact baseline counts, the determinism and leak rows, and the throughput row.
- This ADR adds a Note to ADR 0003 (the response order), ADR 0005 (the response window) and ADR 0011 (the first tier-4 question and its budget). The vision gets Notes.

**Note (2026-10-11, [ADR 0014](0014-goal-aware-hold-policy.md)):** the deferred hold rule is decided. It is an opt-in play policy, `goal-aware-hold`, that both arms play. The default policy still has no hold rule, and its assumptions block still declares the tap-out limit.
