"""Count the distinct Forge vocabulary names (effect APIs, trigger modes,
replacement events, static modes, keywords, cost parts) that the cards of
each greedy step use. Forge's closed vocabulary is the yardstick for how
large ours must grow. Needs forge_scripts/ (forge_fetch.py) and cost.json
(cost.py).

Usage: python forge_vocab.py <audit_dir>"""

import json
import os
import re
import sys
import unicodedata
from collections import Counter

HERE = sys.path[0]
cost = json.load(open(f"{HERE}/cost.json"))
steps = cost["step_names"]  # index 0 = covered today, then steps 1..25


def norm(s):
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]", "", s.lower())


API = re.compile(r"\b(?:SP|AB|DB)\$\s*([A-Za-z]+)")
TRIG = re.compile(r"^T:.*?\bMode\$\s*([A-Za-z]+)")
REPL = re.compile(r"^R:.*?\bEvent\$\s*([A-Za-z]+)")
STAT = re.compile(r"^S:.*?\bMode\$\s*([A-Za-z]+)")
KW = re.compile(r"^K:([A-Za-z][A-Za-z ]*?)(?::|$)")
COST = re.compile(r"\bCost\$\s*([^|]+)")
COSTPART = re.compile(r"(?:^|\s)([A-Za-z]+)(?:<|\b)")
CONDLIKE = re.compile(r"\b(Condition[A-Za-z]*|CheckSVar|IsPresent|ValidTgts|Defined|TgtPrompt)\$")


def vocab(name):
    p = os.path.join(HERE, "forge_scripts", norm(name) + ".txt")
    if not os.path.exists(p):
        return None
    out = set()
    text = open(p, encoding="utf-8", errors="ignore").read()
    for line in text.splitlines():
        line = line.strip()
        for m in API.finditer(line):
            out.add("api:" + m.group(1))
        for rx, tag in ((TRIG, "trigger"), (REPL, "replacement"), (STAT, "static")):
            m = rx.search(line)
            if m:
                out.add(f"{tag}:{m.group(1)}")
        m = KW.search(line)
        if m:
            out.add("keyword:" + m.group(1).strip())
        for m in COST.finditer(line):
            for part in m.group(1).split():
                q = re.match(r"([A-Za-z]+)", part)
                if q and not re.fullmatch(r"[WUBRGCSX]+", q.group(1)):
                    out.add("cost:" + q.group(1))
    return out


cum = set()
cum_names = []
print("step, cards, distinct names cumulative (api / trigger / replacement / static / keyword / cost)")
for i, names in enumerate(steps):
    cum_names += names
    for n in names:
        v = vocab(n)
        if v:
            cum |= v
    by = Counter(x.split(":")[0] for x in cum)
    print(f"  {i:2}: +{len(names)} cards, total {len(cum)}: api {by['api']}, trigger {by['trigger']}, "
          f"replacement {by['replacement']}, static {by['static']}, keyword {by['keyword']}, cost {by['cost']}")
    if i in (0, 10, 23):
        print("     api names:", sorted(x[4:] for x in cum if x.startswith("api:")))
        print("     trigger names:", sorted(x[8:] for x in cum if x.startswith("trigger:")))

# whole sample
allv = set()
r = json.load(open(f"{sys.argv[1]}/results.json", encoding="utf-8"))
for n, c in r["cards"].items():
    if not c["basic"]:
        v = vocab(n)
        if v:
            allv |= v
by = Counter(x.split(":")[0] for x in allv)
print("whole sample:", len(allv), dict(by))
