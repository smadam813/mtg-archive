"""Score the tagged sample: coverage for our engine and Forge, mechanic
weights, and a greedy order of mechanics to add.

Usage: python analyze.py   (reads results.json from classify.py)
"""

import json
import random
import sys
from collections import Counter, defaultdict

sys.path.insert(0, sys.path[0])
from classify import GROUP, SUBSYSTEMS  # noqa: E402

HERE = sys.path[0]
r = json.load(open(f"{HERE}/results.json", encoding="utf-8"))
cards = r["cards"]
slots = r["slots"]
nb = [s for s in slots if not cards[s[2]]["basic"]]
COVERED = {"defined", "inert_exact", "fetch_facet", "expressible"}


def pct(a, b):
    return f"{100 * a / b:.1f}%"


def unit(tag):
    g = GROUP.get(tag)
    return f"sub:{g}" if g else f"eff:{tag}"


def label(u):
    kind, name = u.split(":", 1)
    return SUBSYSTEMS[name] if kind == "sub" else name


out = {}
print("== sample")
by_src = defaultdict(list)
for s in slots:
    by_src[s[0]].append(s)
decks = {(s[0], s[1]) for s in slots}
print("decks", len(decks), "slots", len(slots), "nonbasic slots", len(nb), "unique cards", len(cards),
      "unique nonbasic", sum(1 for c in cards.values() if not c["basic"]))
for src, ss in by_src.items():
    print(f"  {src}: decks {len({s[1] for s in ss})}, slots {len(ss)}, nonbasic {sum(1 for s in ss if not cards[s[2]]['basic'])}")

print("\n== engine coverage (nonbasic slots)")
eng = Counter(cards[s[2]]["engine"] for s in nb)
for k in ["defined", "inert_exact", "fetch_facet", "expressible", "uncovered"]:
    print(f"  {k}: {eng[k]} ({pct(eng[k], len(nb))})")
cov = sum(eng[k] for k in COVERED)
print(f"  covered total: {cov} ({pct(cov, len(nb))}); defined+inert_exact+fetch_facet: {pct(eng['defined'] + eng['inert_exact'] + eng['fetch_facet'], len(nb))}")
allc = sum(1 for s in slots if cards[s[2]]["engine"] in COVERED or cards[s[2]]["basic"])
print(f"  all slots incl. basics covered: {allc}/{len(slots)} ({pct(allc, len(slots))})")
print("  per source:")
for src, ss in by_src.items():
    n = [s for s in ss if not cards[s[2]]["basic"]]
    c = sum(1 for s in n if cards[s[2]]["engine"] in COVERED)
    d = sum(1 for s in n if cards[s[2]]["engine"] == "defined")
    f = sum(1 for s in n if cards[s[2]]["forge"] == "present")
    print(f"    {src}: engine covered {pct(c, len(n))} (defined {pct(d, len(n))}), forge {pct(f, len(n))}")
uniq_nb = [c for c in cards.values() if not c["basic"]]
print("  unique nonbasic covered:", sum(1 for c in uniq_nb if c["engine"] in COVERED), "/", len(uniq_nb))

print("\n== forge coverage")
fc = Counter(cards[s[2]]["forge"] for s in slots)
print("  all slots:", dict(fc), pct(fc["present"], len(slots)))
fn = Counter(cards[s[2]]["forge"] for s in nb)
print("  nonbasic slots:", dict(fn), pct(fn["present"], len(nb)))
print("  unique:", Counter(c["forge"] for c in cards.values()))
print("  scripts with a # TODO comment (unique):", sum(1 for c in cards.values() if c["forge_todo"]))
print("  missing:", [n for n, c in cards.items() if c["forge"] != "present"])

print("\n== needs by subsystem and effect kind (share of nonbasic slots whose card needs it)")
need_units = Counter()
need_tags = Counter()
uniq_units = defaultdict(set)
for s in nb:
    c = cards[s[2]]
    if c["engine"] in COVERED:
        continue
    for u in {unit(t) for t in c["missing"]}:
        need_units[u] += 1
        uniq_units[u].add(s[2])
    for t in c["missing"]:
        need_tags[t] += 1
for u, w in need_units.most_common():
    print(f"  {label(u)}: {w} ({pct(w, len(nb))}), {len(uniq_units[u])} cards")
print("\n  tags:")
for t, w in need_tags.most_common():
    print(f"    {t} [{GROUP.get(t)}]: {w} ({pct(w, len(nb))})")

print("\n== missing-unit count per uncovered card (slot-weighted)")
k = Counter(len({unit(t) for t in cards[s[2]]["missing"]}) for s in nb if cards[s[2]]["engine"] not in COVERED)
print("  ", sorted(k.items()))

print("\n== greedy: add the unit that unlocks the most slots next")
chosen = set()
covered = sum(1 for s in nb if cards[s[2]]["engine"] in COVERED)
steps = []
remaining = [s for s in nb if cards[s[2]]["engine"] not in COVERED]
while remaining and len(steps) < 25:
    best, gain = None, -1
    for u in need_units:
        if u in chosen:
            continue
        trial = chosen | {u}
        g = sum(1 for s in remaining if {unit(t) for t in cards[s[2]]["missing"]} <= trial)
        # tie-break on raw weight among remaining
        key = (g, sum(1 for s in remaining if u in {unit(t) for t in cards[s[2]]["missing"]}))
        if best is None or key > gain:
            best, gain = u, key
    chosen.add(best)
    newly = [s for s in remaining if {unit(t) for t in cards[s[2]]["missing"]} <= chosen]
    remaining = [s for s in remaining if s not in newly]
    covered += len(newly)
    steps.append((label(best), len(newly), covered))
    print(f"  {len(steps):2}. +{label(best)}: unlocks {len(newly)}, cumulative {covered} ({pct(covered, len(nb))})")

print("\n== top cards by slot weight that stay uncovered")
w = Counter(s[2] for s in nb)
for n, c in w.most_common(40):
    if cards[n]["engine"] not in COVERED:
        print(f"  {n} x{c}: {', '.join(cards[n]['missing'])}")

print("\n== spot-check sample (seed 207, 30 unique nonbasic cards)")
rng = random.Random(207)
for n in rng.sample(sorted(n for n, c in cards.items() if not c["basic"]), 30):
    c = cards[n]
    print(f"--- {n} [{c['engine']}] {c['types']}\n    {c['text'][:300]!r}\n    tags: {c['tags']}")

print("\n== cards one tag away from covered (slot-weighted, by that tag)")
one = Counter()
for s in nb:
    c = cards[s[2]]
    if c["engine"] not in COVERED and len(c["missing"]) == 1:
        one[c["missing"][0]] += 1
for t, n in one.most_common(20):
    print(f"  {t}: {n}")

import csv
w = Counter(s[2] for s in slots)
decks_with = defaultdict(set)
for s in slots:
    decks_with[s[2]].add((s[0], s[1]))
with open(f"{HERE}/card-coverage.csv", "w", newline="", encoding="utf-8") as f:
    wr = csv.writer(f)
    wr.writerow(["card", "slots", "decks", "basic", "engine", "missing_tags", "forge"])
    for n, c in sorted(cards.items(), key=lambda kv: (-w[kv[0]], kv[0])):
        wr.writerow([n, w[n], len(decks_with[n]), c["basic"], c["engine"], " ".join(c["missing"]), c["forge"]])

print("\n== lenient view: mana sources whose only gaps are tapped entry or self-damage")
MINOR = {"etb_tapped", "damage", "pay_life", "enters_with", "life_loss"}
len_ok = [s for s in nb if cards[s[2]]["engine"] not in COVERED and set(cards[s[2]]["missing"]) <= MINOR
          and ("Land" in cards[s[2]]["types"] or cards[s[2]]["missing"] == ["damage"])]
print(f"  minor-gap mana sources: {len(len_ok)} slots ({pct(len(len_ok), len(nb))}); lenient covered {pct(cov + len(len_ok), len(nb))}")
lands = [s for s in nb if "Land" in cards[s[2]]["types"].split(" // ")[0]]
nonlands = [s for s in nb if "Land" not in cards[s[2]]["types"].split(" // ")[0]]
print(f"  nonbasic lands: {len(lands)} slots, covered {pct(sum(1 for s in lands if cards[s[2]]['engine'] in COVERED), len(lands))}")
print(f"  nonland cards: {len(nonlands)} slots, covered {pct(sum(1 for s in nonlands if cards[s[2]]['engine'] in COVERED), len(nonlands))}")
