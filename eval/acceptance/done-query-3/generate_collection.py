"""Generate the synthetic ManaBox collection for done-query 3.

The maintainer has no full ManaBox export, so this script stands in for one.
It starts from the done-query-2 collection (seed 78, EDHREC-rank sampled)
and adds the Esper deck's cards, so the user owns the deck they ask about.
The done-query-2 collection already holds the Esper fixes: shocks, original
duals, fetches, checklands, painlands, filter lands, talismans, and signets.

Usage: python generate_collection.py <done-query-2/collection.csv> <decklist.json> <out.csv>
"""

import base64
import csv
import io
import json
import subprocess
import sys

ADDED = "2026-10-01T00:00:00.000Z"

HEADER = [
    "Name", "Set code", "Set name", "Collector number", "Foil", "Rarity",
    "Quantity", "ManaBox ID", "Scryfall ID", "Purchase price", "Misprint",
    "Altered", "Condition", "Language", "Purchase price currency", "Added",
]


def query(names):
    quoted = ", ".join("'" + n.replace("'", "''") + "'" for n in names)
    return f"""
select coalesce(json_agg(json_build_object(
    'name', name,
    'set', upper(raw->>'set'),
    'set_name', raw->>'set_name',
    'collector_number', raw->>'collector_number',
    'rarity', raw->>'rarity',
    'scryfall_id', raw->>'id',
    'usd', raw->'prices'->>'usd'
) order by name), '[]')
from card
where not stale and name in ({quoted})
"""


def fetch_cards(names):
    # psql through docker exec mangles newlines, so the JSON travels base64.
    sql = f"select encode(convert_to(({query(names)})::text, 'UTF8'), 'base64')"
    out = subprocess.run(
        ["docker", "exec", "mtg-archive-postgres-1", "psql", "-U", "postgres",
         "-d", "mtg_archive", "-At", "-c", sql],
        check=True, capture_output=True, text=True,
    ).stdout
    return json.loads(base64.b64decode("".join(out.split())).decode("utf-8"))


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


def main(base_path, decklist_path, out_path):
    with open(base_path, newline="", encoding="utf-8") as f:
        base_rows = list(csv.DictReader(f))
    owned = {r["Name"] for r in base_rows}

    with open(decklist_path, encoding="utf-8") as f:
        decklist = json.load(f)["decklist"]
    wanted = {c["name"]: 1 for c in decklist["commanders"]}
    for entry in decklist["entries"]:
        wanted[entry["card"]["name"]] = entry["quantity"]
    missing = sorted(n for n in wanted if n not in owned)

    added = []
    for card in fetch_cards(missing):
        added.append(row(card, wanted[card["name"]]))
    found = {r["Name"] for r in added}
    unresolved = [n for n in missing if n not in found]
    if unresolved:
        sys.exit(f"no printing found for: {unresolved}")

    buf = io.StringIO()
    writer = csv.DictWriter(buf, fieldnames=HEADER, lineterminator="\n")
    writer.writeheader()
    writer.writerows({k: r.get(k, "") for k in HEADER} for r in base_rows)
    writer.writerows(added)
    with open(out_path, "w", newline="", encoding="utf-8") as f:
        f.write(buf.getvalue())

    print(f"base rows {len(base_rows)}, added rows {len(added)}, "
          f"copies {sum(int(r['Quantity']) for r in base_rows) + sum(r['Quantity'] for r in added)}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3])
