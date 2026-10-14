"""Check Forge script coverage over the whole paper Commander card pool in the archive.

Usage: python forge_pool.py <forge_cardsfolder>
"""

import base64
import json
import subprocess
import sys

sys.path.insert(0, sys.path[0])
from classify import forge_status, load_forge  # noqa: E402

EXCLUDED = ("token", "double_faced_token", "emblem", "planar", "scheme", "vanguard", "art_series", "augment", "host")
QUERY = f"""
select coalesce(json_agg(json_build_object('name', name, 'oracle_text', oracle_text, 'set_type', raw->>'set_type')), '[]')
from card
where not stale
  and raw->'legalities'->>'commander' in ('legal', 'banned')
  and coalesce((raw->>'digital')::boolean, false) = false
  and raw->>'layout' not in ({", ".join(f"'{x}'" for x in EXCLUDED)})
"""
sql = f"select encode(convert_to(({QUERY})::text, 'UTF8'), 'base64')"
out = subprocess.run(["docker", "exec", "mtg-archive-postgres-1", "psql", "-U", "postgres", "-d", "mtg_archive", "-At", "-c", sql],
                     check=True, capture_output=True, text=True).stdout
cards = json.loads(base64.b64decode("".join(out.split())).decode("utf-8"))
index = load_forge(sys.argv[1])
missing = [c["name"] for c in cards if forge_status(c, index)[0] != "present"]
print("pool", len(cards), "missing or stub", len(missing), f"coverage {100 * (1 - len(missing) / len(cards)):.2f}%")
print(sorted(missing)[:80])
