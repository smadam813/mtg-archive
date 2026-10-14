"""Parameter surface of the Forge scripts per greedy step: distinct
parameter keys (Key$), and the share of cards that use a computed amount
(Count$ SVar), a condition, or a target filter. Our DSL's parameter
grammar must grow to the same surface.

Usage: python forge_params.py   (after forge_fetch.py and cost.py)"""

import json
import os
import re
import sys
import unicodedata

HERE = sys.path[0]
steps = json.load(open(f"{HERE}/cost.json"))["step_names"]


def norm(s):
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]", "", s.lower())


KEY = re.compile(r"\b([A-Z][A-Za-z]+)\$")


def read(name):
    p = os.path.join(HERE, "forge_scripts", norm(name) + ".txt")
    return open(p, encoding="utf-8", errors="ignore").read() if os.path.exists(p) else None


keys = set()
seen = 0
flags = {"count": 0, "condition": 0, "target": 0, "ailogic": 0, "any_ability": 0}
for i, names in enumerate(steps):
    for n in names:
        t = read(n)
        if t is None:
            continue
        seen += 1
        body = "\n".join(l for l in t.splitlines() if not l.startswith(("Name:", "ManaCost:", "Types:", "PT:", "Oracle:", "Loyalty:", "Colors:")))
        keys |= set(KEY.findall(body))
        flags["count"] += "Count$" in t
        flags["condition"] += bool(re.search(r"Condition|CheckSVar|IsPresent\$", t))
        flags["target"] += bool(re.search(r"ValidTgts\$|TgtPrompt\$|TargetType\$", t))
        flags["ailogic"] += "AILogic$" in t or "AIPreference" in t or "AITgts" in t
        flags["any_ability"] += bool(re.search(r"^(A|T|S|R|K|SVar):", t, re.M))
    if i in (0, 10, 23, 25):
        print(f"step {i}: cards {seen}, distinct parameter keys {len(keys)}; "
              + ", ".join(f"{k} {v} ({100*v/seen:.0f}%)" for k, v in flags.items()))
