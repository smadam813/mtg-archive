"""Write sample-cards.csv: layout, front-face type line, and face names for each card of the audit sample.

The card list is the `card` column of the audit's card-coverage.csv (branch research/mechanics-coverage-audit).
Needs the compose stack's Postgres container (project `mtg-archive`).
Usage: python dump_sample_cards.py <card-coverage.csv>
"""

import base64
import csv
import json
import subprocess
import sys

names = [r["card"] for r in csv.DictReader(open(sys.argv[1], encoding="utf-8"))]
QUERY = """
select json_agg(x) from (
  select distinct on (c.name) c.name, c.type_line, c.oracle_text, c.raw->>'layout' as layout,
         (select json_agg(f->>'name') from jsonb_array_elements(c.raw->'card_faces') f) as faces
  from card c
  order by c.name, c.stale, c.raw->>'layout' in ('token', 'double_faced_token', 'emblem')
) x
"""
# psql through docker exec mangles newlines, so the JSON travels base64.
sql = f"select encode(convert_to(({QUERY})::text, 'UTF8'), 'base64')"
out = subprocess.run(
    ["docker", "exec", "mtg-archive-postgres-1", "psql", "-U", "postgres", "-d", "mtg_archive", "-At", "-c", sql],
    check=True, capture_output=True, text=True,
).stdout
rows = {r["name"]: r for r in json.loads(base64.b64decode("".join(out.split())).decode("utf-8"))}

missing = [n for n in names if n not in rows]
if missing:
    sys.exit(f"not in the archive: {missing}")
with open(f"{sys.path[0]}/sample-cards.csv", "w", newline="", encoding="utf-8") as f:
    wr = csv.writer(f)
    wr.writerow(["card", "layout", "front_type_line", "faces", "has_text"])
    for n in names:
        r = rows[n]
        wr.writerow([n, r["layout"], r["type_line"].split(" // ")[0], " // ".join(r["faces"] or []),
                     bool((r["oracle_text"] or "").strip())])
print(len(names), "cards")
