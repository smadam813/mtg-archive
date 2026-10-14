# Engine gates

The engine gates of the XMage migration (spec §6) run on the host, against the compose stack, through `tools/EngineGate`. They are not CI tests, because the throughput bar is set for this host.

## Before a measurement

1. Quiet the host. Close games, streams and other heavy work: they slow runs by 15-25%. `throughput` samples host CPU for 5 s and refuses a host above 15% busy unless you pass `--force`.
2. Start the stack:

   ```bash
   docker compose -p mtg-archive -f compose.yaml -f compose.gpu.yml up -d --build xmage service
   ```

3. Wait until `docker compose -p mtg-archive ps` shows `xmage` and `service` healthy. The sidecar warms up for up to two minutes.
4. Make sure the card feature profile table is full. After a change to the derivation, run a forced ingest first (`archive-admin` skill).

## Throughput (spec §6.5)

```bash
dotnet run --project tools/EngineGate -- throughput
```

It posts `eval/acceptance/done-query-2/rerun-request.json` (10,000 runs, horizon 10) twice. The request as written is the gate. The second post sets every goal leaf due at the horizon, so no run stops early. Each request appends one row to `throughput.md`. Run it again on each XMage tag move and on each pilot change that adds round trips.

The tier-3 reference row times `eval/engine/tier-3-request.json`: the done-query 2 final 99, Pithing Needle on the battlefield by turn 4, the default disruption profile, and 10,000 pairs.

```bash
dotnet run --project tools/EngineGate -- throughput --request eval/engine/tier-3-request.json
```

A request with a disruption profile runs paired, so it posts once and appends one row. The row counts runs as pairs, adds games per second, and gives the floor of each arm.

The tier-4 row (ADR 0013) times `eval/engine/tier-4-request.json`: the tier-3 request with `"disruption_mode": "interactive"`. It is the interactive budget: at 10,000 pairs it must finish in under 120 s with at most 1% engine errors, or the interactive cap drops to 5,000 pairs. Run it again on each shim change that adds round trips to the interactive path.

```bash
dotnet run --project tools/EngineGate -- throughput --request eval/engine/tier-4-request.json
```

## Coverage (roadmap carried-forward item 5)

```bash
dotnet run --project tools/EngineGate -- coverage eval/acceptance/done-query-2/rerun-request.json
```

It posts each request with one run and appends the deck's engine coverage to `coverage.md`.

The parity, determinism and leak gates live in `eval/parity/` (see its `README.md`).
