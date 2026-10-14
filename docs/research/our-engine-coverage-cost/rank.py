"""EDHREC rank of the card in each nonbasic slot: the rank cutoff that
holds a share of slots. A cutoff of R is about R cards to author for a deck
the sample never saw, if decks draw cards the way the sample does.

Usage: python rank.py <audit_dir>"""

import json
import sys

r = json.load(open(f"{sys.argv[1]}/results.json", encoding="utf-8"))
cards = r["cards"]
nb = [s for s in r["slots"] if not cards[s[2]]["basic"]]
ranks = sorted((cards[s[2]].get("rank") or 10**9) for s in nb)
N = len(ranks)
print("slots", N, "with no rank", sum(1 for x in ranks if x == 10**9))
for q in [0.25, 0.5, 0.75, 0.9, 0.95]:
    print(f"  {int(q*100)}% of nonbasic slots hold a card with edhrec_rank <= {ranks[int(q*N) - 1]}")
# per source, excluding the two repo decks
for src in ["edhrec-average", "archidekt"]:
    rs = sorted((cards[s[2]].get("rank") or 10**9) for s in nb if s[0] == src)
    print(src, {f"{int(q*100)}%": rs[int(q*len(rs)) - 1] for q in [0.5, 0.9]})
# nonland only
rs = sorted((cards[s[2]].get("rank") or 10**9) for s in nb if "Land" not in cards[s[2]]["types"].split(" // ")[0])
print("nonland", {f"{int(q*100)}%": rs[int(q*len(rs)) - 1] for q in [0.5, 0.9]})
