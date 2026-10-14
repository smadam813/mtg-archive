# data/

This directory holds the curated, git-versioned source-of-truth files
(`banlist-events.json`, `banlist-categories.json`, `game-changers/`, `bracket-spec/`, `curated/`), the local popularity exports (see below), plus source snapshots (CR TXT,
banlist archives) and ManaBox CSV drops. compose.yaml mounts it read-only at
`/data` in the service container — the service archives content into Postgres
and never writes here.

## Popularity exports

The popularity exports are local files that git ignores (ADR 0012). The
layout is `popularity/<format>/<window-start>_<window-end>.csv`: one Untapped.gg
card-data file per Brawl format, in a directory named by the format's wire name
(`brawl`, `competitive_brawl`), with `yyyy-MM-dd` dates. A fresh clone has no
exports, so Brawl and Competitive Brawl use the EDHREC proxy until the maintainer
adds them.
