# Closed-vocabulary Effect DSL with a definition/policy seam

**Status:** superseded by [ADR 0011](0011-xmage-simulation-engine.md). The Effect DSL serves our goldfish engine only until XMage passes the parity gate.

Staple semantics are JSON card definitions over a closed vocabulary of engine-implemented effect primitives — unknown primitive names are hard failures — rather than a general effect language. Forge sustained ~33,700 card scripts on ~200 closed primitives (growing ~7 per year for 12.5 years); XMage went general (Java per card) and still converged on a shared effect library. The vocabulary grows deliberately: richer parameters on an existing primitive first, then composition, then a new primitive.

Definitions declare what a player may choose and never choose. The engine pauses at each choice point — mana payment included — and a single pilot-policy interface makes every pick, with swappable implementations (greedy solver as the default). AI-hint fields are banned from the definition schema outright: Forge embeds `AILogic` in 2,460 card scripts and pays with per-card AI code. The engine derives rules categories (e.g. mana abilities) from structure, so authors cannot mislabel them.

Reading the JSON is a review first pass, not the gate. A definition joins the staple set only when schema validation passes and its check — a separate fixture with its own schema — passes, with checks asserting every Oracle clause and review done beside the pinned Oracle text.
