"""Generate the synthetic ManaBox collection for done-query 2.

The maintainer has no full ManaBox export, so this script stands in for one.
It is seeded and reads the live archive, so the same snapshot and seed give
the same file.

Composition:
- Base: the maintainer's real Golos deck, verbatim ManaBox rows.
- Forced: Pithing Needle. The query presumes the user owns it.
- Sampled: every paper card that is legal or banned in Commander, owned with
  probability OWN_TOP * exp(-(rank - 1) / RANK_SCALE) + OWN_FLOOR by EDHREC
  rank, damped by PRICE_CAP / usd for cards above PRICE_CAP.
- Basics: BASICS_EACH of each basic land type.

Usage: python generate_collection.py <real_deck.csv> <out.csv>
"""

import base64
import csv
import io
import json
import math
import random
import subprocess
import sys

SEED = 78
OWN_TOP = 0.92
RANK_SCALE = 1500
OWN_FLOOR = 0.02
PRICE_CAP = 15.0
BASICS_EACH = 30
FORCED = ["Pithing Needle"]
BASICS = ["Plains", "Island", "Swamp", "Mountain", "Forest"]
ADDED = "2026-09-24T00:00:00.000Z"
EXCLUDED_LAYOUTS = (
    "token", "double_faced_token", "emblem", "planar", "scheme",
    "vanguard", "art_series", "augment", "host",
)

HEADER = [
    "Name", "Set code", "Set name", "Collector number", "Foil", "Rarity",
    "Quantity", "ManaBox ID", "Scryfall ID", "Purchase price", "Misprint",
    "Altered", "Condition", "Language", "Purchase price currency", "Added",
]

QUERY = f"""
select coalesce(json_agg(json_build_object(
    'name', name,
    'rank', edhrec_rank,
    'set', upper(raw->>'set'),
    'set_name', raw->>'set_name',
    'collector_number', raw->>'collector_number',
    'rarity', raw->>'rarity',
    'scryfall_id', raw->>'id',
    'usd', raw->'prices'->>'usd'
) order by name), '[]')
from card
where not stale
  and raw->'legalities'->>'commander' in ('legal', 'banned')
  and coalesce((raw->>'digital')::boolean, false) = false
  and raw->>'layout' not in ({", ".join(f"'{l}'" for l in EXCLUDED_LAYOUTS)})
"""


def fetch_cards():
    # psql through docker exec mangles newlines, so the JSON travels base64.
    sql = f"select encode(convert_to(({QUERY})::text, 'UTF8'), 'base64')"
    out = subprocess.run(
        ["docker", "exec", "mtg-archive-postgres-1", "psql", "-U", "postgres",
         "-d", "mtg_archive", "-At", "-c", sql],
        check=True, capture_output=True, text=True,
    ).stdout
    return json.loads(base64.b64decode("".join(out.split())).decode("utf-8"))


def own_probability(card):
    rank = card["rank"]
    p = OWN_FLOOR if rank is None else OWN_TOP * math.exp(-(rank - 1) / RANK_SCALE) + OWN_FLOOR
    usd = float(card["usd"]) if card["usd"] else 0.0
    if usd > PRICE_CAP:
        p *= PRICE_CAP / usd
    return p


def quantity(card, rng):
    if card["rarity"] == "common":
        return rng.choices([1, 2, 3, 4], weights=[50, 25, 15, 10])[0]
    if card["rarity"] == "uncommon":
        return rng.choices([1, 2], weights=[75, 25])[0]
    return 1


def row(card, qty):
    return {
        "Name": card["name"], "Set code": card["set"], "Set name": card["set_name"],
        "Collector number": card["collector_number"], "Foil": "normal",
        "Rarity": card["rarity"], "Quantity": qty, "ManaBox ID": "",
        "Scryfall ID": card["scryfall_id"], "Purchase price": card["usd"] or "",
        "Misprint": "false", "Altered": "false", "Condition": "near_mint",
        "Language": "en", "Purchase price currency": "USD" if card["usd"] else "",
        "Added": ADDED,
    }


def main(real_deck_path, out_path):
    with open(real_deck_path, newline="", encoding="utf-8") as f:
        real_rows = list(csv.DictReader(f))
    owned = {r["Name"] for r in real_rows}

    rng = random.Random(SEED)
    rows = []
    for card in fetch_cards():
        name = card["name"]
        draw = rng.random()
        if name in BASICS:
            rows.append(row(card, BASICS_EACH))
        elif name in owned:
            continue
        elif name in FORCED or draw < own_probability(card):
            rows.append(row(card, quantity(card, rng)))

    buf = io.StringIO()
    writer = csv.DictWriter(buf, fieldnames=HEADER, lineterminator="\n")
    writer.writeheader()
    writer.writerows({k: r.get(k, "") for k in HEADER} for r in real_rows)
    writer.writerows(rows)
    with open(out_path, "w", newline="", encoding="utf-8") as f:
        f.write(buf.getvalue())

    print(f"real rows {len(real_rows)}, generated rows {len(rows)}, "
          f"copies {sum(int(r['Quantity']) for r in real_rows) + sum(r['Quantity'] for r in rows)}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
