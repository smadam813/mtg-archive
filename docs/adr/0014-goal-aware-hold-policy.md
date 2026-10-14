# The goal-aware hold policy: an opt-in play policy for interactive disruption

ADR 0013 left the main phase as it was. The goal-aware greedy pilot taps out, and it casts a response card such as Heroic Intervention at sorcery speed. The protection parity case `bq1-wrenn-and-six-responses-t3-interactive` measured the cost. The baseline arm meets the goal in 77.5% of 10,000 runs. The disrupted arm meets it in 3.45%, and the answered rate is 0.003. Interactive disruption understated a deck with protection.

**The goal-aware hold policy (`goal-aware-hold`) is a second play policy.** It is opt-in. It plays as the goal-aware greedy (ADR 0003) with two holds on the main phase. `goal-aware-greedy` stays the default and does not change.

## The request

- `POST /simulate` takes one optional top-level member, `play_policy`: `goal-aware-greedy` (the default) or `goal-aware-hold`. A request without the member plays as before, and its envelope does not change.
- `goal-aware-hold` needs `disruption_mode: interactive`. Without it, the request is a 400 `invalid-play-policy`. An unknown value is the same 400, with the extension member `allowed`.
- The envelope's `policies.play` echoes the policy that ran. The mulligan policy does not change.
- Both arms play the policy. The baseline arm and the disrupted arm are identical up to the first disruption card, and the pilot never sees the disruption profile (ADR 0005).
- Only the `deck-check` skill sends `play_policy`, and only in the tier-4 hold run. That run happens when `disruption.response_cards` is not empty.

## The two holds

- **Card hold, from turn 1.** The pilot never casts a response card in the main phase when its only rank is the "anything else" tier. A response card that the goal-aware order ranks higher plays as it does under the default policy: it draws, tutors or ramps, or the goal wants it. A response card is a card whose profile proves an answer to a card of the disruption table (glossary: Response card).
- **Mana reserve, while the goal holds.** While the whole goal predicate holds now, the pilot keeps a reserve. An `all_of` goal holds only when every leaf holds.
  - The reserve is the mana value of the cheapest response card in hand that answers removal or a wipe aimed at a goal card. With no such card in hand, the reserve is zero.
  - A goal card is a permanent that answers an `on_battlefield` leaf due this turn or later. A goal with only `cast` leaves has no goal card, so it keeps no reserve.
  - A counterspell that can counter the removal counts. A grant counts when it answers Atomize or Rout and covers a goal card. Atomize and Rout are the removal and wipe cards of the disruption card table.
  - The pilot takes a main-phase action that costs mana only when the untapped mana left after it is at least the reserve. Mana that the action adds counts.
  - The reserve counts mana value only, not colors.
- **Rebuild.** When the goal stops holding (a disruption card hit), the reserve stops. The pilot plays as the default policy until the goal holds again.

The response window keeps the greedy response rule of ADR 0013.

## Floors and assumptions

- The floor label stays ">= X%, greedy policy". The hold policy is a greedy policy too: it does no lookahead and reads only the pilot view. Its floor is a floor under that policy.
- The interactive assumptions block drops the tap-out sentence of the response-rule entry and adds three entries: the policy, the reserve and the limits. The limits are:
  - The counterspell path does not change, because the reserve starts only after the goal holds.
  - An early `all_of` leaf has no protection.
  - The held mana may lack the answer's colors, because the engine picks the sources that pay.

## Measured effect

On the hold case (10,000 pairs, base seed 20260928) the baseline arm meets the goal in 80.1% of runs and the disrupted arm in 7.3%. The answered rate is 0.073. The baseline arm differs from the greedy case (77.5%) because both arms play the hold policy: the card hold changes play before any disruption card.

## The parity gate

- A parity case names its play policy with `play_policy`. A case with no policy is `goal-aware-greedy`. The baseline records the policy as the case wrote it, and a changed policy makes the entry stale. A policy outside the list stops the gate when the cases load, and so does `goal-aware-hold` in a case that is not interactive.
- `bq1-wrenn-and-six-responses-t3-interactive-hold` runs the protection case under `goal-aware-hold`. It gets determinism and leak rows. The owner approves its first baseline on the pull request.
- Every earlier case equals `baseline.json`.

## Considered options

- **A hold rule in the default policy.** Rejected: it would move every floor, and the arms of the tier-3 and tier-4 cases would play differently before the first disruption card.
- **A hold in the disrupted arm only.** Rejected: the pilot would play differently before the first disruption card, and the resilience delta would measure the hold and not the disruption.
- **A hold in every mode.** Rejected: in abstracted disruption and tier 2 a held card answers nothing, so the hold only lowers the floor.
- **A color-aware reserve, or a shim change to the mana payment.** Rejected for this decision. The assumptions block declares the limit. A later issue can lift it.
- **A hold for the counterspell path**, such as delaying the goal cast to keep a counter open. Rejected for this decision: the policy would need lookahead to know when a counter is worth the delay.
- **A new top-level mode instead of a policy member.** Rejected: the policy is a choice of the pilot, and ADR 0003 already names the pilot's play policy in the envelope.

## Consequences

- The `IPilotPolicy` interface gains a second implementation, `GoalAwareHoldPilot`, beside `GoalAwarePilot`.
- The parity case format gains `play_policy`, and the baseline entry gains the same member.
- This ADR adds a Note to ADR 0003 (a second play policy) and to ADR 0013 (the deferred hold rule is decided).
