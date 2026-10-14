"""Build the deck sample for the mechanics coverage audit.

Sources:
- Repo: the maintainer's real Golos deck (eval/acceptance/done-query-2/collection.csv,
  rows not stamped with the generator's Added date). It is also the done-query 1 fixture.
- Repo: the done-query 2 Obeka deck (obeka.txt, copied from the verdict comment).
- EDHREC: average decks of the top N commanders, "Past 2 Years" list.
- Archidekt: the most-viewed public Commander decks of size 100, updated since CUTOFF.

Usage: python fetch_sample.py <repo_root> <out_sample.json>
"""

import csv
import json
import sys
import time
import urllib.request

TOP_EDHREC = 25
TOP_ARCHIDEKT = 25
ARCHIDEKT_CUTOFF = "2025-01-01"
UA = {"User-Agent": "mtg-archive-research/1.0 (coverage audit)"}
GENERATOR_ADDED = "2026-09-24T00:00:00.000Z"


def get(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))


def golos(repo):
    path = f"{repo}/eval/acceptance/done-query-2/collection.csv"
    with open(path, newline="", encoding="utf-8") as f:
        rows = [r for r in csv.DictReader(f) if r["Added"] != GENERATOR_ADDED]
    return {
        "source": "collection",
        "id": "golos-real-deck",
        "name": "Golos, Tireless Pilgrim (maintainer's real deck; done-query 1 fixture)",
        "commanders": ["Golos, Tireless Pilgrim"],
        "cards": [[r["Name"], int(r["Quantity"])] for r in rows],
    }


def obeka(here):
    with open(f"{here}/obeka.txt", encoding="utf-8") as f:
        names = [line.strip() for line in f if line.strip()]
    return {
        "source": "done-query",
        "id": "done-query-2-obeka",
        "name": "Obeka, Brute Chronologist (done-query 2 final list)",
        "commanders": ["Obeka, Brute Chronologist"],
        "cards": [[n, 1] for n in names],
    }


def edhrec():
    top = get("https://json.edhrec.com/pages/commanders/year.json")
    views = top["container"]["json_dict"]["cardlists"][0]["cardviews"][:TOP_EDHREC]
    decks = []
    for v in views:
        d = get(f"https://json.edhrec.com/pages/average-decks/{v['slug']}.json")
        cards = []
        for group in d["deck"]["cards"].values():
            cards.extend([n, q] for n, q in group)
        commanders = [n for n, _ in d["deck"]["commander_v2"]]
        cards.extend([n, q] for n, q in d["deck"]["commander_v2"])
        decks.append({
            "source": "edhrec-average",
            "id": v["slug"],
            "name": f"EDHREC average deck: {v['name']} (rank {v['rank']}, {v['num_decks']} decks)",
            "commanders": commanders,
            "cards": cards,
        })
        time.sleep(1)
    return decks


def archidekt():
    decks = []
    page = 1
    while len(decks) < TOP_ARCHIDEKT and page <= 20:
        listing = get(f"https://archidekt.com/api/decks/v3/?deckFormat=3&orderBy=-viewCount&pageSize=50&page={page}")
        for row in listing["results"]:
            if len(decks) >= TOP_ARCHIDEKT:
                break
            if row["size"] != 100 or row["updatedAt"][:10] < ARCHIDEKT_CUTOFF or row.get("theorycrafted"):
                continue
            d = get(f"https://archidekt.com/api/decks/{row['id']}/")
            time.sleep(1)
            included = {c["name"] for c in d["categories"] if c["includedInDeck"]}
            premier = {c["name"] for c in d["categories"] if c.get("isPremier")}
            cards, commanders = [], []
            for c in d["cards"]:
                cats = c.get("categories") or []
                if cats and not any(x in included for x in cats):
                    continue
                oc = c["card"]["oracleCard"]
                cards.append([oc["name"], c["quantity"], oc["uid"]])
                if any(x in premier for x in cats):
                    commanders.append(oc["name"])
            total = sum(q for _, q, _ in cards)
            if total != 100:
                continue
            decks.append({
                "source": "archidekt",
                "id": str(row["id"]),
                "name": f"Archidekt {row['id']}: {row['name']} ({row['viewCount']} views, updated {row['updatedAt'][:10]})",
                "commanders": commanders,
                "cards": cards,
            })
        page += 1
        time.sleep(1)
    return decks


def main(repo, out):
    here = sys.path[0]
    decks = [golos(repo), obeka(here)] + edhrec() + archidekt()
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"fetched": time.strftime("%Y-%m-%d"), "decks": decks}, f, indent=1, ensure_ascii=False)
    for d in decks:
        print(d["source"], d["id"], sum(c[1] for c in d["cards"]))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
