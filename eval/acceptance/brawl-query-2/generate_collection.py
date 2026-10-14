"""Generate the synthetic ManaBox collection for Brawl acceptance query 2.

The collection is the done-query-2 paper collection plus a seeded sample of
Standard-legal cards, with no digital-only card. The script is seeded and
reads a frozen pool file, so the same pool and seed give the same file.

Composition:
- Base: every row of done-query-2/collection.csv, verbatim.
- Sampled: every card in the pool (Standard-legal, not digital-only) that the
  base does not own and that is no basic land, owned with probability
  OWN_BY_RARITY[rarity]. The quantity follows done-query 2: a common takes
  1 to 4 copies, an uncommon 1 or 2, a rare or mythic 1.

The pool comes from the Scryfall search API (`legal:standard -is:digital`,
one paper printing per card). Fetch it once with `--fetch` into a scratch
file, then generate from that file. The pool is not committed: Standard
changes, so collection.csv is the frozen output. The committed file came from
a pool of 5,164 cards fetched on 2026-10-05 (Oracle Cards bulk of
2026-10-04T21:01:57Z).

Usage:
  python generate_collection.py --fetch <standard-pool.jsonl>
  python generate_collection.py <done-query-2/collection.csv> <standard-pool.jsonl> <out.csv>
"""

import csv
import io
import json
import random
import sys
import time
import urllib.parse
import urllib.request

SEED = 295
OWN_BY_RARITY = {"common": 0.55, "uncommon": 0.40, "rare": 0.22, "mythic": 0.12}
BASICS = {"Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes"}
ADDED = "2026-10-05T00:00:00.000Z"
QUERY = "legal:standard -is:digital"
EXCLUDED_LAYOUTS = {
    "token", "double_faced_token", "emblem", "planar", "scheme",
    "vanguard", "art_series", "augment", "host",
}

HEADER = [
    "Name", "Set code", "Set name", "Collector number", "Foil", "Rarity",
    "Quantity", "ManaBox ID", "Scryfall ID", "Purchase price", "Misprint",
    "Altered", "Condition", "Language", "Purchase price currency", "Added",
]

POOL_KEYS = ("name", "set", "set_name", "collector_number", "rarity", "id", "usd",
             "type_line", "cmc", "color_identity", "standardbrawl", "digital", "layout")


def fetch(pool_path):
    """Writes the pool: one compact JSON object per card, sorted by name."""
    url = "https://api.scryfall.com/cards/search?" + urllib.parse.urlencode(
        {"q": QUERY, "unique": "cards", "order": "name"})
    headers = {"User-Agent": "mtg-archive-acceptance/0.1", "Accept": "application/json"}
    cards = []
    while url:
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as r:
            page = json.load(r)
        cards.extend(page["data"])
        url = page.get("next_page") if page.get("has_more") else None
        time.sleep(0.15)  # Scryfall asks for 50-100 ms between requests.
    with open(pool_path, "w", newline="\n", encoding="utf-8") as f:
        for c in sorted(cards, key=lambda c: c["name"]):
            if c.get("digital") or c["layout"] in EXCLUDED_LAYOUTS:
                continue
            row = {
                "name": c["name"], "set": c["set"].upper(), "set_name": c["set_name"],
                "collector_number": c["collector_number"], "rarity": c["rarity"], "id": c["id"],
                "usd": c["prices"].get("usd"), "type_line": c.get("type_line", ""), "cmc": c.get("cmc", 0),
                "color_identity": c.get("color_identity", []),
                "standardbrawl": c["legalities"].get("standardbrawl"),
                "digital": c.get("digital", False), "layout": c["layout"],
            }
            f.write(json.dumps({k: row[k] for k in POOL_KEYS}, ensure_ascii=False) + "\n")
    print(f"pool {len(cards)} cards -> {pool_path}")


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
        "Scryfall ID": card["id"], "Purchase price": card["usd"] or "",
        "Misprint": "false", "Altered": "false", "Condition": "near_mint",
        "Language": "en", "Purchase price currency": "USD" if card["usd"] else "",
        "Added": ADDED,
    }


def generate(base_path, pool_path, out_path):
    with open(base_path, newline="", encoding="utf-8") as f:
        base_rows = list(csv.DictReader(f))
    owned = {r["Name"] for r in base_rows}
    with open(pool_path, encoding="utf-8") as f:
        pool = [json.loads(line) for line in f if line.strip()]

    rng = random.Random(SEED)
    rows = []
    for card in pool:
        draw = rng.random()
        if card["name"] in owned or card["name"] in BASICS or card["digital"]:
            continue
        if draw < OWN_BY_RARITY.get(card["rarity"], 0.0):
            rows.append(row(card, quantity(card, rng)))

    buf = io.StringIO()
    writer = csv.DictWriter(buf, fieldnames=HEADER, lineterminator="\n")
    writer.writeheader()
    writer.writerows({k: r.get(k, "") for k in HEADER} for r in base_rows)
    writer.writerows(rows)
    with open(out_path, "w", newline="", encoding="utf-8") as f:
        f.write(buf.getvalue())

    print(f"base rows {len(base_rows)}, sampled rows {len(rows)}, "
          f"copies {sum(int(r['Quantity']) for r in base_rows) + sum(r['Quantity'] for r in rows)}")


if __name__ == "__main__":
    if sys.argv[1] == "--fetch":
        fetch(sys.argv[2])
    else:
        generate(sys.argv[1], sys.argv[2], sys.argv[3])
