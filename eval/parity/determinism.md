# Determinism

`EngineGate determinism` appends one row per parity deck (spec §6.3; see `README.md`). Every seed plays in two separate sidecar processes; each must give the same game digest. A game that ends in an engine error proves nothing about play and fails the row. First different is the first run index whose digests differ.

| Date (UTC) | Engine | Deck | Seeds | Different | Engine errors | First different | Verdict |
|---|---|---|---|---|---|---|---|
| 2026-09-29 00:08 | xmage xmage-1.4.61V1+2b804525 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-09-29 00:08 | xmage xmage-1.4.61V1+2b804525 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-09-30 04:16 | xmage xmage-1.4.61V1+96331334 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-09-30 04:17 | xmage xmage-1.4.61V1+96331334 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-09-30 12:34 | xmage xmage-1.4.61V1+beee0dd6 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-09-30 12:34 | xmage xmage-1.4.61V1+beee0dd6 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-01 01:23 | xmage xmage-1.4.61V1+09f8be52 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-01 01:24 | xmage xmage-1.4.61V1+09f8be52 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-05 03:05 | xmage xmage-1.4.61V1+ec6a3165 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-05 03:06 | xmage xmage-1.4.61V1+ec6a3165 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-05 03:53 | xmage xmage-1.4.61V1+ec6a3165 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-05 03:54 | xmage xmage-1.4.61V1+ec6a3165 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-05 03:54 | xmage xmage-1.4.61V1+ec6a3165 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:54 | xmage xmage-1.4.61V1+ec6a3165 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:55 | xmage xmage-1.4.61V1+ec6a3165 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:55 | xmage xmage-1.4.61V1+ec6a3165 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:55 | xmage xmage-1.4.61V1+ec6a3165 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:56 | xmage xmage-1.4.61V1+ec6a3165 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:56 | xmage xmage-1.4.61V1+ec6a3165 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:56 | xmage xmage-1.4.61V1+ec6a3165 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:56 | xmage xmage-1.4.61V1+ec6a3165 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:11 | xmage xmage-1.4.61V1+9014521b | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:11 | xmage xmage-1.4.61V1+9014521b | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:12 | xmage xmage-1.4.61V1+9014521b | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:12 | xmage xmage-1.4.61V1+9014521b | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:13 | xmage xmage-1.4.61V1+9014521b | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:13 | xmage xmage-1.4.61V1+9014521b | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:13 | xmage xmage-1.4.61V1+9014521b | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:13 | xmage xmage-1.4.61V1+9014521b | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:25 | xmage xmage-1.4.62V1+a3d8e53a | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:25 | xmage xmage-1.4.62V1+a3d8e53a | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:26 | xmage xmage-1.4.62V1+a3d8e53a | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:26 | xmage xmage-1.4.62V1+a3d8e53a | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:27 | xmage xmage-1.4.62V1+a3d8e53a | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:27 | xmage xmage-1.4.62V1+a3d8e53a | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:27 | xmage xmage-1.4.62V1+a3d8e53a | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:27 | xmage xmage-1.4.62V1+a3d8e53a | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:27 | xmage xmage-1.4.62V1+a3d8e53a | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:27 | xmage xmage-1.4.62V1+a3d8e53a | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:27 | xmage xmage-1.4.62V1+a3d8e53a | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:28 | xmage xmage-1.4.62V1+a3d8e53a | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:28 | xmage xmage-1.4.62V1+a3d8e53a | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:29 | xmage xmage-1.4.62V1+a3d8e53a | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:29 | xmage xmage-1.4.62V1+a3d8e53a | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:29 | xmage xmage-1.4.62V1+a3d8e53a | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:29 | xmage xmage-1.4.62V1+a3d8e53a | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:38 | xmage xmage-1.4.62V1+82bc4524 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:38 | xmage xmage-1.4.62V1+82bc4524 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:38 | xmage xmage-1.4.62V1+82bc4524 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:38 | xmage xmage-1.4.62V1+82bc4524 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:39 | xmage xmage-1.4.62V1+82bc4524 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:39 | xmage xmage-1.4.62V1+82bc4524 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:40 | xmage xmage-1.4.62V1+82bc4524 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:40 | xmage xmage-1.4.62V1+82bc4524 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:40 | xmage xmage-1.4.62V1+82bc4524 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:46 | xmage xmage-1.4.62V1+923aae6e | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:47 | xmage xmage-1.4.62V1+923aae6e | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:47 | xmage xmage-1.4.62V1+923aae6e | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:47 | xmage xmage-1.4.62V1+923aae6e | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:47 | xmage xmage-1.4.62V1+923aae6e | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:48 | xmage xmage-1.4.62V1+923aae6e | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:48 | xmage xmage-1.4.62V1+923aae6e | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:48 | xmage xmage-1.4.62V1+923aae6e | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:48 | xmage xmage-1.4.62V1+923aae6e | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:39 | xmage xmage-1.4.62V1+57914a98 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:39 | xmage xmage-1.4.62V1+57914a98 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:39 | xmage xmage-1.4.62V1+57914a98 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:39 | xmage xmage-1.4.62V1+57914a98 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:40 | xmage xmage-1.4.62V1+57914a98 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:40 | xmage xmage-1.4.62V1+57914a98 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:40 | xmage xmage-1.4.62V1+57914a98 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:41 | xmage xmage-1.4.62V1+57914a98 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:41 | xmage xmage-1.4.62V1+57914a98 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:25 | xmage xmage-1.4.62V1+06f1cfce | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:26 | xmage xmage-1.4.62V1+06f1cfce | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:26 | xmage xmage-1.4.62V1+06f1cfce | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:26 | xmage xmage-1.4.62V1+06f1cfce | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:27 | xmage xmage-1.4.62V1+06f1cfce | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:27 | xmage xmage-1.4.62V1+06f1cfce | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:27 | xmage xmage-1.4.62V1+06f1cfce | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:27 | xmage xmage-1.4.62V1+06f1cfce | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:27 | xmage xmage-1.4.62V1+06f1cfce | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:16 | xmage xmage-1.4.62V1+06f1cfce | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:16 | xmage xmage-1.4.62V1+06f1cfce | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:16 | xmage xmage-1.4.62V1+06f1cfce | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:16 | xmage xmage-1.4.62V1+06f1cfce | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:17 | xmage xmage-1.4.62V1+06f1cfce | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:17 | xmage xmage-1.4.62V1+06f1cfce | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:17 | xmage xmage-1.4.62V1+06f1cfce | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:17 | xmage xmage-1.4.62V1+06f1cfce | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:18 | xmage xmage-1.4.62V1+06f1cfce | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:58 | xmage xmage-1.4.62V1+5841eec8 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:59 | xmage xmage-1.4.62V1+5841eec8 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:59 | xmage xmage-1.4.62V1+5841eec8 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:59 | xmage xmage-1.4.62V1+5841eec8 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:59 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:00 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:00 | xmage xmage-1.4.62V1+5841eec8 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:00 | xmage xmage-1.4.62V1+5841eec8 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:00 | xmage xmage-1.4.62V1+5841eec8 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:46 | xmage xmage-1.4.62V1+5841eec8 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:46 | xmage xmage-1.4.62V1+5841eec8 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:46 | xmage xmage-1.4.62V1+5841eec8 | dq2-final-99 | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:47 | xmage xmage-1.4.62V1+5841eec8 | dq2-final-99-needle-t4-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:47 | xmage xmage-1.4.62V1+5841eec8 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:47 | xmage xmage-1.4.62V1+5841eec8 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:48 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:48 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:48 | xmage xmage-1.4.62V1+5841eec8 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:48 | xmage xmage-1.4.62V1+5841eec8 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:49 | xmage xmage-1.4.62V1+5841eec8 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 16:45 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six-responses | 1000 | 0 | 0 | – | pass |
| 2026-10-11 16:46 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six-responses-t3-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:01 | xmage xmage-1.4.62V1+146e3b92 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:01 | xmage xmage-1.4.62V1+146e3b92 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:01 | xmage xmage-1.4.62V1+146e3b92 | dq2-final-99 | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:01 | xmage xmage-1.4.62V1+146e3b92 | dq2-final-99-needle-t4-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:02 | xmage xmage-1.4.62V1+146e3b92 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:02 | xmage xmage-1.4.62V1+146e3b92 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:02 | xmage xmage-1.4.62V1+146e3b92 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:03 | xmage xmage-1.4.62V1+146e3b92 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:03 | xmage xmage-1.4.62V1+146e3b92 | bq1-wrenn-and-six-responses | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:04 | xmage xmage-1.4.62V1+146e3b92 | bq1-wrenn-and-six-responses-t3-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:04 | xmage xmage-1.4.62V1+146e3b92 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:04 | xmage xmage-1.4.62V1+146e3b92 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:04 | xmage xmage-1.4.62V1+146e3b92 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:42 | xmage xmage-1.4.62V1+0243e949 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:42 | xmage xmage-1.4.62V1+0243e949 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:42 | xmage xmage-1.4.62V1+0243e949 | dq2-final-99 | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:43 | xmage xmage-1.4.62V1+0243e949 | dq2-final-99-needle-t4-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:43 | xmage xmage-1.4.62V1+0243e949 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:43 | xmage xmage-1.4.62V1+0243e949 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:44 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:44 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:45 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six-responses | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:45 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six-responses-t3-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:45 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six-responses-t3-interactive-hold | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:46 | xmage xmage-1.4.62V1+0243e949 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:46 | xmage xmage-1.4.62V1+0243e949 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:46 | xmage xmage-1.4.62V1+0243e949 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:57 | xmage xmage-1.4.62V1+019bfe96 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:57 | xmage xmage-1.4.62V1+019bfe96 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:57 | xmage xmage-1.4.62V1+019bfe96 | dq2-final-99 | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:58 | xmage xmage-1.4.62V1+019bfe96 | dq2-final-99-needle-t4-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:58 | xmage xmage-1.4.62V1+019bfe96 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:58 | xmage xmage-1.4.62V1+019bfe96 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:59 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:59 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:00 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six-responses | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:00 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six-responses-t3-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:00 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six-responses-t3-interactive-hold | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:01 | xmage xmage-1.4.62V1+019bfe96 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:01 | xmage xmage-1.4.62V1+019bfe96 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:01 | xmage xmage-1.4.62V1+019bfe96 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
