# Leak check

`EngineGate leak` appends one row per parity deck (spec §6.4; see `README.md`). Every seed plays with the game-copies changes of patch 0002 on, then off, in the same image; each must give the same game digest, or a shared library card leaked a playable calculation into the game. A game that ends in an engine error fails the row.

In the builds 5841eec8, 146e3b92 and 0243e949 the two rows failed: `dq2-final-99` (19 seeds) and `dq2-final-99-needle-t4-interactive` (8 seeds). A playable calculation draws object IDs. The game-copies changes alter how many IDs it draws. The shim breaks mana-source ties by ID, so XMage paid for a spell with a different source. Patch 0002 now resets the object-ID stream when each calculation ends. Both rows pass in builds 019bfe96 and 5e6c91b6, which have the reset.

| Date (UTC) | Engine | Deck | Seeds | Different | Engine errors | First different | Verdict |
|---|---|---|---|---|---|---|---|
| 2026-09-29 00:07 | xmage xmage-1.4.61V1+2b804525 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-09-29 00:07 | xmage xmage-1.4.61V1+2b804525 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-09-30 04:17 | xmage xmage-1.4.61V1+96331334 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-09-30 04:17 | xmage xmage-1.4.61V1+96331334 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-09-30 12:34 | xmage xmage-1.4.61V1+beee0dd6 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-09-30 12:35 | xmage xmage-1.4.61V1+beee0dd6 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-01 01:24 | xmage xmage-1.4.61V1+09f8be52 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-01 01:25 | xmage xmage-1.4.61V1+09f8be52 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-05 03:55 | xmage xmage-1.4.61V1+ec6a3165 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-05 03:55 | xmage xmage-1.4.61V1+ec6a3165 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-05 03:56 | xmage xmage-1.4.61V1+ec6a3165 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:57 | xmage xmage-1.4.61V1+ec6a3165 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:57 | xmage xmage-1.4.61V1+ec6a3165 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:57 | xmage xmage-1.4.61V1+ec6a3165 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:58 | xmage xmage-1.4.61V1+ec6a3165 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:58 | xmage xmage-1.4.61V1+ec6a3165 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:59 | xmage xmage-1.4.61V1+ec6a3165 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:59 | xmage xmage-1.4.61V1+ec6a3165 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-05 05:59 | xmage xmage-1.4.61V1+ec6a3165 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:08 | xmage xmage-1.4.61V1+9014521b | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:08 | xmage xmage-1.4.61V1+9014521b | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:09 | xmage xmage-1.4.61V1+9014521b | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:09 | xmage xmage-1.4.61V1+9014521b | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:10 | xmage xmage-1.4.61V1+9014521b | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:10 | xmage xmage-1.4.61V1+9014521b | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:11 | xmage xmage-1.4.61V1+9014521b | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-06 22:11 | xmage xmage-1.4.61V1+9014521b | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:22 | xmage xmage-1.4.62V1+a3d8e53a | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:22 | xmage xmage-1.4.62V1+a3d8e53a | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:23 | xmage xmage-1.4.62V1+a3d8e53a | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:23 | xmage xmage-1.4.62V1+a3d8e53a | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:24 | xmage xmage-1.4.62V1+a3d8e53a | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:24 | xmage xmage-1.4.62V1+a3d8e53a | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:25 | xmage xmage-1.4.62V1+a3d8e53a | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 00:25 | xmage xmage-1.4.62V1+a3d8e53a | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:30 | xmage xmage-1.4.62V1+a3d8e53a | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:30 | xmage xmage-1.4.62V1+a3d8e53a | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:31 | xmage xmage-1.4.62V1+a3d8e53a | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:31 | xmage xmage-1.4.62V1+a3d8e53a | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:32 | xmage xmage-1.4.62V1+a3d8e53a | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:32 | xmage xmage-1.4.62V1+a3d8e53a | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:33 | xmage xmage-1.4.62V1+a3d8e53a | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:33 | xmage xmage-1.4.62V1+a3d8e53a | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 08:35 | xmage xmage-1.4.62V1+a3d8e53a | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:40 | xmage xmage-1.4.62V1+82bc4524 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:41 | xmage xmage-1.4.62V1+82bc4524 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:41 | xmage xmage-1.4.62V1+82bc4524 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:41 | xmage xmage-1.4.62V1+82bc4524 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:42 | xmage xmage-1.4.62V1+82bc4524 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:42 | xmage xmage-1.4.62V1+82bc4524 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:43 | xmage xmage-1.4.62V1+82bc4524 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:43 | xmage xmage-1.4.62V1+82bc4524 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 09:43 | xmage xmage-1.4.62V1+82bc4524 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:49 | xmage xmage-1.4.62V1+923aae6e | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:49 | xmage xmage-1.4.62V1+923aae6e | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:49 | xmage xmage-1.4.62V1+923aae6e | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:50 | xmage xmage-1.4.62V1+923aae6e | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:50 | xmage xmage-1.4.62V1+923aae6e | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:51 | xmage xmage-1.4.62V1+923aae6e | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:51 | xmage xmage-1.4.62V1+923aae6e | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:51 | xmage xmage-1.4.62V1+923aae6e | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 10:52 | xmage xmage-1.4.62V1+923aae6e | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:41 | xmage xmage-1.4.62V1+57914a98 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:42 | xmage xmage-1.4.62V1+57914a98 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:42 | xmage xmage-1.4.62V1+57914a98 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:42 | xmage xmage-1.4.62V1+57914a98 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:43 | xmage xmage-1.4.62V1+57914a98 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:43 | xmage xmage-1.4.62V1+57914a98 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:44 | xmage xmage-1.4.62V1+57914a98 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:44 | xmage xmage-1.4.62V1+57914a98 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 11:44 | xmage xmage-1.4.62V1+57914a98 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:28 | xmage xmage-1.4.62V1+06f1cfce | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:28 | xmage xmage-1.4.62V1+06f1cfce | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:29 | xmage xmage-1.4.62V1+06f1cfce | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:29 | xmage xmage-1.4.62V1+06f1cfce | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:29 | xmage xmage-1.4.62V1+06f1cfce | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:30 | xmage xmage-1.4.62V1+06f1cfce | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:30 | xmage xmage-1.4.62V1+06f1cfce | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:31 | xmage xmage-1.4.62V1+06f1cfce | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 12:31 | xmage xmage-1.4.62V1+06f1cfce | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:18 | xmage xmage-1.4.62V1+06f1cfce | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:18 | xmage xmage-1.4.62V1+06f1cfce | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:19 | xmage xmage-1.4.62V1+06f1cfce | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:19 | xmage xmage-1.4.62V1+06f1cfce | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:20 | xmage xmage-1.4.62V1+06f1cfce | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:20 | xmage xmage-1.4.62V1+06f1cfce | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:21 | xmage xmage-1.4.62V1+06f1cfce | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:21 | xmage xmage-1.4.62V1+06f1cfce | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 13:21 | xmage xmage-1.4.62V1+06f1cfce | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:01 | xmage xmage-1.4.62V1+5841eec8 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:01 | xmage xmage-1.4.62V1+5841eec8 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:01 | xmage xmage-1.4.62V1+5841eec8 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:02 | xmage xmage-1.4.62V1+5841eec8 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:02 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:03 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:03 | xmage xmage-1.4.62V1+5841eec8 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:03 | xmage xmage-1.4.62V1+5841eec8 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:04 | xmage xmage-1.4.62V1+5841eec8 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:49 | xmage xmage-1.4.62V1+5841eec8 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:49 | xmage xmage-1.4.62V1+5841eec8 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:50 | xmage xmage-1.4.62V1+5841eec8 | dq2-final-99 | 1000 | 19 | 0 | 56 | fail |
| 2026-10-11 14:50 | xmage xmage-1.4.62V1+5841eec8 | dq2-final-99-needle-t4-interactive | 1000 | 8 | 0 | 75 | fail |
| 2026-10-11 14:50 | xmage xmage-1.4.62V1+5841eec8 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:51 | xmage xmage-1.4.62V1+5841eec8 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:51 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:52 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:52 | xmage xmage-1.4.62V1+5841eec8 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:53 | xmage xmage-1.4.62V1+5841eec8 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 14:53 | xmage xmage-1.4.62V1+5841eec8 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 16:46 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six-responses | 1000 | 0 | 0 | – | pass |
| 2026-10-11 16:47 | xmage xmage-1.4.62V1+5841eec8 | bq1-wrenn-and-six-responses-t3-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:05 | xmage xmage-1.4.62V1+146e3b92 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:05 | xmage xmage-1.4.62V1+146e3b92 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:05 | xmage xmage-1.4.62V1+146e3b92 | dq2-final-99 | 1000 | 19 | 0 | 56 | fail |
| 2026-10-11 18:06 | xmage xmage-1.4.62V1+146e3b92 | dq2-final-99-needle-t4-interactive | 1000 | 8 | 0 | 75 | fail |
| 2026-10-11 18:06 | xmage xmage-1.4.62V1+146e3b92 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:06 | xmage xmage-1.4.62V1+146e3b92 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:07 | xmage xmage-1.4.62V1+146e3b92 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:07 | xmage xmage-1.4.62V1+146e3b92 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:08 | xmage xmage-1.4.62V1+146e3b92 | bq1-wrenn-and-six-responses | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:09 | xmage xmage-1.4.62V1+146e3b92 | bq1-wrenn-and-six-responses-t3-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:09 | xmage xmage-1.4.62V1+146e3b92 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:09 | xmage xmage-1.4.62V1+146e3b92 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 18:09 | xmage xmage-1.4.62V1+146e3b92 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:47 | xmage xmage-1.4.62V1+0243e949 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:47 | xmage xmage-1.4.62V1+0243e949 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:47 | xmage xmage-1.4.62V1+0243e949 | dq2-final-99 | 1000 | 19 | 0 | 56 | fail |
| 2026-10-11 21:48 | xmage xmage-1.4.62V1+0243e949 | dq2-final-99-needle-t4-interactive | 1000 | 8 | 0 | 75 | fail |
| 2026-10-11 21:48 | xmage xmage-1.4.62V1+0243e949 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:49 | xmage xmage-1.4.62V1+0243e949 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:49 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:50 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:50 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six-responses | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:51 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six-responses-t3-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:52 | xmage xmage-1.4.62V1+0243e949 | bq1-wrenn-and-six-responses-t3-interactive-hold | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:52 | xmage xmage-1.4.62V1+0243e949 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:52 | xmage xmage-1.4.62V1+0243e949 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 21:52 | xmage xmage-1.4.62V1+0243e949 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:50 | xmage xmage-1.4.62V1+019bfe96 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:51 | xmage xmage-1.4.62V1+019bfe96 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:51 | xmage xmage-1.4.62V1+019bfe96 | dq2-final-99 | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:52 | xmage xmage-1.4.62V1+019bfe96 | dq2-final-99-needle-t4-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:52 | xmage xmage-1.4.62V1+019bfe96 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:52 | xmage xmage-1.4.62V1+019bfe96 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:53 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:54 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:54 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six-responses | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:55 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six-responses-t3-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:55 | xmage xmage-1.4.62V1+019bfe96 | bq1-wrenn-and-six-responses-t3-interactive-hold | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:56 | xmage xmage-1.4.62V1+019bfe96 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:56 | xmage xmage-1.4.62V1+019bfe96 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-11 23:56 | xmage xmage-1.4.62V1+019bfe96 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:19 | xmage xmage-1.4.62V1+5e6c91b6 | dq2-obeka | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:19 | xmage xmage-1.4.62V1+5e6c91b6 | dq2-needle-t4-disrupted | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:20 | xmage xmage-1.4.62V1+5e6c91b6 | dq2-final-99 | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:20 | xmage xmage-1.4.62V1+5e6c91b6 | dq2-final-99-needle-t4-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:20 | xmage xmage-1.4.62V1+5e6c91b6 | dq1-golos | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:21 | xmage xmage-1.4.62V1+5e6c91b6 | brawl-seek-conjure | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:21 | xmage xmage-1.4.62V1+5e6c91b6 | bq1-wrenn-and-six (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:22 | xmage xmage-1.4.62V1+5e6c91b6 | bq1-wrenn-and-six (competitive_brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:23 | xmage xmage-1.4.62V1+5e6c91b6 | bq1-wrenn-and-six-responses | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:23 | xmage xmage-1.4.62V1+5e6c91b6 | bq1-wrenn-and-six-responses-t3-interactive | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:24 | xmage xmage-1.4.62V1+5e6c91b6 | bq1-wrenn-and-six-responses-t3-interactive-hold | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:24 | xmage xmage-1.4.62V1+5e6c91b6 | bq2-jolly-balloon-man | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:25 | xmage xmage-1.4.62V1+5e6c91b6 | brawl-calibration (brawl) | 1000 | 0 | 0 | – | pass |
| 2026-10-12 00:25 | xmage xmage-1.4.62V1+5e6c91b6 | brawl-calibration (competitive_brawl) | 1000 | 0 | 0 | – | pass |
