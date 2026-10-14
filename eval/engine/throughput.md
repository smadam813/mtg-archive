# Engine throughput

`EngineGate throughput` appends two rows per call (spec §6.5; see `README.md`). The gate row times the done-query 2 request as written. Its 10,000 runs at horizon 10 must finish in under 120 s through `/simulate` on this host. At most 1% of them can end in an engine error. The full-horizon row sets every goal leaf due at the horizon, so no run stops early. It is information, not the gate.

The 11:36 and 11:37 rows ran the phase 3B shim, with the hand-mana payment for spirit guides, on a busy host (`--force`). They show the old version string, because the version did not yet cover the shim sources. The 22:51 and 22:52 rows measure the same play on a quiet host, under the version that names it, and give the same floors. New rows go at the end of the file, so keep notes above the table.

A request with a disruption profile runs paired and appends one row, with no full-horizon row. Its runs are pairs, and each pair plays two games, so the row gives pairs and games per second, the floor of each arm, and the 1% bar over games. The 2026-10-11 08:23 row is the tier-3 reference: `tier-3-request.json` (the done-query 2 final 99, Pithing Needle on the battlefield by turn 4, the default disruption profile, 10,000 pairs). It ran on a stack rebuilt from main. The tier-4 budget compares against it.

The 2026-10-11 14:20 row is the tier-4 budget of ADR 0013: `tier-4-request.json` (the tier-3 request in interactive mode, 10,000 pairs). It ran the sidecar and the service built from the branch, against a throwaway copy of the archive after the migration and a forced ingest. It finished in 78.9 s with 0 engine errors, under the 120 s bar, so the interactive cap stays 10,000 pairs.

| Date (UTC) | Engine | Request | Runs | Horizon | Seconds | Runs/s | Engine errors | Floor | Host busy | Verdict |
|---|---|---|---|---|---|---|---|---|---|---|
| 2026-09-28 01:29 | xmage xmage-1.4.61V1+d2a4f2c5 | rerun-request.json | 10000 | 10 | 32.6 | 307 | 0 | >= 18.1%, greedy policy | 10% | pass |
| 2026-09-28 01:31 | xmage xmage-1.4.61V1+d2a4f2c5 | rerun-request.json (full horizon) | 10000 | 10 | 68.2 | 147 | 0 | >= 72.2%, greedy policy | 10% | info |
| 2026-09-28 11:36 | xmage xmage-1.4.61V1+d2a4f2c5 | rerun-request.json | 10000 | 10 | 32.6 | 307 | 0 | >= 19.5%, greedy policy | 22% | pass |
| 2026-09-28 11:37 | xmage xmage-1.4.61V1+d2a4f2c5 | rerun-request.json (full horizon) | 10000 | 10 | 68.1 | 147 | 0 | >= 73.1%, greedy policy | 22% | info |
| 2026-09-28 22:51 | xmage xmage-1.4.61V1+d240d809 | rerun-request.json | 10000 | 10 | 29.5 | 339 | 0 | >= 19.5%, greedy policy | 3% | pass |
| 2026-09-28 22:52 | xmage xmage-1.4.61V1+d240d809 | rerun-request.json (full horizon) | 10000 | 10 | 63.6 | 157 | 0 | >= 73.1%, greedy policy | 3% | info |
| 2026-09-29 00:26 | xmage xmage-1.4.61V1+2b804525 | rerun-request.json | 10000 | 10 | 30.0 | 333 | 0 | >= 19.5%, greedy policy | 3% | pass |
| 2026-09-29 00:27 | xmage xmage-1.4.61V1+2b804525 | rerun-request.json (full horizon) | 10000 | 10 | 66.5 | 150 | 0 | >= 73.1%, greedy policy | 3% | info |
| 2026-09-30 19:16 | xmage xmage-1.4.61V1+beee0dd6 | rerun-request.json | 10000 | 10 | 30.8 | 325 | 0 | >= 19.5%, greedy policy | 4% | pass |
| 2026-09-30 19:17 | xmage xmage-1.4.61V1+beee0dd6 | rerun-request.json (full horizon) | 10000 | 10 | 62.0 | 161 | 0 | >= 73%, greedy policy | 4% | info |
| 2026-10-01 01:41 | xmage xmage-1.4.61V1+09f8be52 | rerun-request.json | 10000 | 10 | 27.8 | 360 | 0 | >= 19.5%, greedy policy | 24% | pass |
| 2026-10-01 01:42 | xmage xmage-1.4.61V1+09f8be52 | rerun-request.json (full horizon) | 10000 | 10 | 69.9 | 143 | 0 | >= 73%, greedy policy | 24% | info |
| 2026-10-11 00:46 | xmage xmage-1.4.62V1+a3d8e53a | rerun-request.json | 10000 | 10 | 25.2 | 396 | 0 | >= 20.1%, greedy policy | 2% | pass |
| 2026-10-11 00:47 | xmage xmage-1.4.62V1+a3d8e53a | rerun-request.json (full horizon) | 10000 | 10 | 59.5 | 168 | 0 | >= 73.3%, greedy policy | 2% | info |
| 2026-10-11 08:23 | xmage xmage-1.4.62V1+a3d8e53a | tier-3-request.json | 10000 pairs | 10 | 85.0 | 118 pairs/s, 235 games/s | 0 | baseline >= 68%, greedy policy; disrupted >= 0.4%, greedy policy | 2% | pass |
| 2026-10-11 09:47 | xmage xmage-1.4.62V1+82bc4524 | rerun-request.json | 10000 | 10 | 25.7 | 390 | 0 | >= 20.1%, greedy policy | 3% | pass |
| 2026-10-11 09:48 | xmage xmage-1.4.62V1+82bc4524 | rerun-request.json (full horizon) | 10000 | 10 | 57.4 | 174 | 0 | >= 73.1%, greedy policy | 3% | info |
| 2026-10-11 10:52 | xmage xmage-1.4.62V1+923aae6e | rerun-request.json | 10000 | 10 | 25.6 | 391 | 0 | >= 20.1%, greedy policy | 3% | pass |
| 2026-10-11 10:53 | xmage xmage-1.4.62V1+923aae6e | rerun-request.json (full horizon) | 10000 | 10 | 57.1 | 175 | 0 | >= 73.1%, greedy policy | 3% | info |
| 2026-10-11 11:46 | xmage xmage-1.4.62V1+57914a98 | rerun-request.json | 10000 | 10 | 26.0 | 385 | 0 | >= 20.1%, greedy policy | 3% | pass |
| 2026-10-11 11:47 | xmage xmage-1.4.62V1+57914a98 | rerun-request.json (full horizon) | 10000 | 10 | 57.4 | 174 | 0 | >= 73.1%, greedy policy | 3% | info |
| 2026-10-11 12:32 | xmage xmage-1.4.62V1+06f1cfce | rerun-request.json | 10000 | 10 | 25.6 | 391 | 0 | >= 20.1%, greedy policy | 2% | pass |
| 2026-10-11 12:33 | xmage xmage-1.4.62V1+06f1cfce | rerun-request.json (full horizon) | 10000 | 10 | 57.3 | 174 | 0 | >= 73.1%, greedy policy | 2% | info |
| 2026-10-11 13:22 | xmage xmage-1.4.62V1+06f1cfce | rerun-request.json | 10000 | 10 | 26.1 | 383 | 0 | >= 20.1%, greedy policy | 1% | pass |
| 2026-10-11 13:23 | xmage xmage-1.4.62V1+06f1cfce | rerun-request.json (full horizon) | 10000 | 10 | 58.0 | 172 | 0 | >= 73.1%, greedy policy | 1% | info |
| 2026-10-11 14:20 | xmage xmage-1.4.62V1+5841eec8 | tier-4-request.json | 10000 pairs | 10 | 78.9 | 127 pairs/s, 253 games/s | 0 | baseline >= 68%, greedy policy; disrupted >= 6.1%, greedy policy | 2% | pass |
| 2026-10-11 18:10 | xmage xmage-1.4.62V1+146e3b92 | rerun-request.json | 10000 | 10 | 26.4 | 378 | 0 | >= 20.1%, greedy policy | 2% | pass |
| 2026-10-11 18:11 | xmage xmage-1.4.62V1+146e3b92 | rerun-request.json (full horizon) | 10000 | 10 | 59.9 | 167 | 0 | >= 73.1%, greedy policy | 2% | info |
