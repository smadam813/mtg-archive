"""Dump the archive's card rows for classify.py into cards.json (not committed: about 20 MB).

Needs the compose stack's Postgres container (project `mtg-archive`).
Usage: python dump_cards.py
"""

import base64
import json
import subprocess
import sys

QUERY = """
select json_agg(json_build_object(
    'id', c.id, 'oracle_id', c.oracle_id, 'name', c.name, 'mana_cost', c.mana_cost,
    'type_line', c.type_line, 'oracle_text', c.oracle_text, 'keywords', c.keywords,
    'power', c.power, 'layout', c.raw->>'layout', 'faces', c.raw->'card_faces', 'stale', c.stale,
    'produced', p.produced_colors, 'fetch', p.fetch_land_types, 'fetch_activation', p.fetch_activation,
    'enters_tapped', p.enters_tapped, 'rank', c.edhrec_rank, 'staple', (sd.card_id is not null)))
from card c
left join source_profile p on p.card_id = c.id
left join staple_definition sd on sd.card_id = c.id
"""

# psql through docker exec mangles newlines, so the JSON travels base64.
sql = f"select encode(convert_to(({QUERY})::text, 'UTF8'), 'base64')"
out = subprocess.run(
    ["docker", "exec", "mtg-archive-postgres-1", "psql", "-U", "postgres", "-d", "mtg_archive", "-At", "-c", sql],
    check=True, capture_output=True, text=True,
).stdout
cards = json.loads(base64.b64decode("".join(out.split())).decode("utf-8"))
with open(f"{sys.path[0]}/cards.json", "w", encoding="utf-8") as f:
    json.dump(cards, f)
print(len(cards), "cards,", sum(1 for c in cards if c["staple"]), "with a staple definition")
