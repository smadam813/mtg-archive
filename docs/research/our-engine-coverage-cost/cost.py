"""Authoring load per step of the audit's greedy order.

Reads the audit's results.json (classify.py output) and repeats analyze.py's
greedy order. For each step it counts the unique cards that the step makes
coverable and that still need a hand-authored definition.

Usage: python cost.py <audit_dir> > cost.txt
<audit_dir> is docs/research/mechanics-coverage-audit with the results.json
that its classify.py writes. Output cost.json lands beside this script.
"""

import json
import sys
from collections import Counter, defaultdict

AUDIT = sys.argv[1]
HERE = sys.path[0]
sys.path.insert(0, AUDIT)
from classify import GROUP, SUBSYSTEMS  # noqa: E402

r = json.load(open(f"{AUDIT}/results.json", encoding="utf-8"))
cards = r["cards"]
nb = [s for s in r["slots"] if not cards[s[2]]["basic"]]
N = len(nb)
COVERED = {"defined", "inert_exact", "fetch_facet", "expressible"}
NO_AUTHORING = {"defined", "inert_exact", "fetch_facet"}
# A land whose only gaps are these can take its behavior from a source-profile
# facet, as fetch lands do today: no per-card definition.
FACET_TAGS = {"etb_tapped", "enters_with", "pay_life", "damage", "life_loss"}


def unit(tag):
    g = GROUP.get(tag)
    return f"sub:{g}" if g else f"eff:{tag}"


def label(u):
    kind, name = u.split(":", 1)
    return SUBSYSTEMS[name] if kind == "sub" else name


def is_land(name):
    return "Land" in cards[name]["types"].split(" // ")[0]


def facet_land(name):
    c = cards[name]
    return is_land(name) and set(c["missing"]) <= FACET_TAGS


slot_w = Counter(s[2] for s in nb)


def clauses(name):
    """Clause count as the clause map splits it: at lines and sentences,
    reminder text removed."""
    import re
    t = re.sub(r"\([^)]*\)", "", cards[name]["text"])
    parts = []
    for line in t.split("\n"):
        parts += [p for p in re.split(r"(?<=[.!?])\s+", line.strip()) if p.strip()]
    return max(1, len(parts))


defined = [n for n, c in cards.items() if c["engine"] == "defined"]
print("== clause count, defined staples in the sample:", len(defined),
      f"mean {sum(clauses(n) for n in defined)/len(defined):.2f}")
units_of = {n: {unit(t) for t in c["missing"]} for n, c in cards.items()}

# ------------------------------------------------ today
print("== today")
base = Counter(cards[s[2]]["engine"] for s in nb)
print("  slots by class:", dict(base))
uniq = Counter(c["engine"] for n, c in cards.items() if not c["basic"])
print("  unique by class:", dict(uniq))

# ------------------------------------------------ greedy, as analyze.py
need_units = Counter()
for s in nb:
    if cards[s[2]]["engine"] not in COVERED:
        for u in units_of[s[2]]:
            need_units[u] += 1

chosen = set()
covered = sum(1 for s in nb if cards[s[2]]["engine"] in COVERED)
remaining = [s for s in nb if cards[s[2]]["engine"] not in COVERED]
cum_cards = sum(1 for n, c in cards.items() if c["engine"] == "expressible")
cum_cards_facet = cum_cards
print("\n== greedy steps: new slots, new unique cards to author (all hand / lands by facet)")
expr = [n for n, c in cards.items() if c["engine"] == "expressible"]
print(f"  step 0: expressible today, unauthored: {cum_cards} cards, {base['expressible']} slots, "
      f"{sum(clauses(n) for n in expr)} clauses")
rows = []
step_names = [sorted(n for n, c in cards.items() if not c["basic"] and c["engine"] in COVERED)]
for step in range(1, 26):
    best, gain = None, None
    for u in need_units:
        if u in chosen:
            continue
        trial = chosen | {u}
        g = sum(1 for s in remaining if units_of[s[2]] <= trial)
        key = (g, sum(1 for s in remaining if u in units_of[s[2]]))
        if gain is None or key > gain:
            best, gain = u, key
    chosen.add(best)
    newly = [s for s in remaining if units_of[s[2]] <= chosen]
    remaining = [s for s in remaining if s not in newly]
    covered += len(newly)
    names = sorted({s[2] for s in newly}, key=lambda n: (-slot_w[n], n))
    lands = [n for n in names if is_land(n)]
    facet = [n for n in names if facet_land(n)]
    cum_cards += len(names)
    cum_cards_facet += len(names) - len(facet)
    singles = sum(1 for n in names if slot_w[n] == 1)
    step_names.append(names)
    cl = sum(clauses(n) for n in names) / max(1, len(names))
    rows.append((step, label(best), len(newly), covered, len(names), len(lands), len(facet), singles, cum_cards, cum_cards_facet, round(cl, 2)))
    print(f"  {step:2}. {label(best)}: +{len(newly)} slots -> {100*covered/N:.1f}%; "
          f"+{len(names)} cards ({len(lands)} lands, {len(facet)} facet lands, {singles} in one slot, "
          f"{cl:.2f} clauses each, {sum(clauses(n) for n in names)} clauses); "
          f"cumulative to author {cum_cards} (facet route {cum_cards_facet}); top: {', '.join(names[:6])}")

# ------------------------------------------------ frequency tail
print("\n== frequency tail (nonbasic)")
n1 = sum(1 for n, w in slot_w.items() if w == 1)
print(f"  unique nonbasic {len(slot_w)}, cards in exactly one slot {n1}")
print(f"  Good-Turing unseen mass n1/N = {n1}/{N} = {100*n1/N:.1f}% of a new deck's nonbasic slots hold a card the sample never saw")
acc = 0
marks = {0.5: None, 0.75: None, 0.9: None}
for i, (n, w) in enumerate(slot_w.most_common(), 1):
    acc += w
    for m in marks:
        if marks[m] is None and acc >= m * N:
            marks[m] = i
print("  unique cards, most frequent first, to hold a share of sample slots:", {f"{int(m*100)}%": v for m, v in marks.items()})

# ------------------------------------------------ cheap gains
print("\n== one-tag-away cards, unique counts")
one = defaultdict(list)
for n, c in cards.items():
    if c["basic"] or c["engine"] in COVERED:
        continue
    if len(c["missing"]) == 1:
        one[c["missing"][0]].append(n)
for t in ["etb_tapped", "damage"]:
    ns = sorted(one[t], key=lambda n: -slot_w[n])
    print(f"  {t}: {len(ns)} cards, {sum(slot_w[n] for n in ns)} slots, lands {sum(1 for n in ns if is_land(n))}")
    print("    ", "; ".join(f"{n} x{slot_w[n]}" for n in ns[:40]))

# facet-only lands overall (any subset of FACET_TAGS)
fl = [n for n, c in cards.items() if not c["basic"] and c["engine"] not in COVERED and facet_land(n)]
print(f"\n  lands whose gaps are all in {sorted(FACET_TAGS)}: {len(fl)} cards, {sum(slot_w[n] for n in fl)} slots "
      f"({100*sum(slot_w[n] for n in fl)/N:.1f}%)")
tagsets = Counter(" ".join(sorted(cards[n]["missing"])) for n in fl)
for k, v in tagsets.most_common():
    print(f"    {k}: {v} cards, {sum(slot_w[n] for n in fl if ' '.join(sorted(cards[n]['missing'])) == k)} slots")
nonland_damage = [n for n in one["damage"] if not is_land(n)]
print("  nonland one-tag damage:", "; ".join(f"{n} x{slot_w[n]}" for n in sorted(nonland_damage, key=lambda n: -slot_w[n])))

json.dump({"rows": rows, "step_names": step_names},open(f"{HERE}/cost.json", "w"), indent=1)
