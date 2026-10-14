# Default disruption profile

A resilience question asks how a deck performs under disruption. The simulate request takes that disruption as a `disruption_profile`: a list of `{ kind, turn }` events. The service supplies no default. This file does.

When the user asks for resilience without naming the disruption, send this profile:

```json
[
  { "kind": "counterspell", "turn": 3 },
  { "kind": "targeted_removal", "turn": 4 }
]
```

Add `board_wipe` only in two cases:

- The user asks for it.
- The goal is a wide board (several creatures or tokens on the battlefield).

When you add it, state the event and its turn.

The same profile serves interactive disruption: send it with `"disruption_mode": "interactive"`, and a scripted opponent casts each event as a real card.

The event kinds are a closed vocabulary: `counterspell`, `targeted_removal`, `board_wipe`. An unknown kind is a request error. If the user names disruption outside this vocabulary, say the archive cannot model it. Offer the nearest kind.

Always state the profile as an assumption in the answer, next to the resilience delta it shaped. Example:

> Assumed disruption: a counterspell on turn 3 and targeted removal on turn 4.

The rule for stating assumptions is in `answer-contract.md`.
