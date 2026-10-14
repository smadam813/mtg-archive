"""Fetch the Forge scripts of the sample's nonbasic cards at Forge 95dc682b
into ./forge_scripts/ (read-only analysis input, not committed: Forge is
GPL-3.0).

Usage: python forge_fetch.py <audit_dir>   (needs the gh CLI)"""

import json
import os
import re
import subprocess
import sys
import unicodedata
import urllib.request
from concurrent.futures import ThreadPoolExecutor

HERE = sys.path[0]
REF = "95dc682b"
OUT = os.path.join(HERE, "forge_scripts")
os.makedirs(OUT, exist_ok=True)


def gh(path):
    return json.loads(subprocess.run(["gh", "api", path], capture_output=True, text=True, check=True).stdout)


def norm(s):
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]", "", s.lower())


index_file = os.path.join(HERE, "forge_index.json")
if os.path.exists(index_file):
    index = json.load(open(index_file))
else:
    top = gh(f"repos/Card-Forge/forge/contents/forge-gui/res/cardsfolder?ref={REF}")
    index = {}
    for d in top:
        if d["type"] != "dir":
            continue
        tree = gh(f"repos/Card-Forge/forge/git/trees/{d['sha']}")
        for e in tree["tree"]:
            if e["path"].endswith(".txt"):
                index.setdefault(norm(e["path"][:-4]), f"{d['name']}/{e['path']}")
    json.dump(index, open(index_file, "w"))
print("forge files", len(index))

r = json.load(open(f"{sys.argv[1]}/results.json", encoding="utf-8"))
cards = r["cards"]
names = [n for n, c in cards.items() if not c["basic"]]
todo, missing = [], []
for n in names:
    key = norm(n.split(" // ")[0])
    p = index.get(key) or index.get(norm(n))
    if p:
        todo.append((n, p))
    else:
        missing.append(n)
print("mapped", len(todo), "unmapped", len(missing), missing[:20])


def fetch(item):
    n, p = item
    dest = os.path.join(OUT, norm(n) + ".txt")
    if os.path.exists(dest):
        return
    url = f"https://raw.githubusercontent.com/Card-Forge/forge/{REF}/forge-gui/res/cardsfolder/{p}"
    for _ in range(3):
        try:
            data = urllib.request.urlopen(url, timeout=30).read()
            open(dest, "wb").write(data)
            return
        except Exception as e:  # noqa: BLE001
            err = e
    print("fail", n, err)


with ThreadPoolExecutor(8) as ex:
    list(ex.map(fetch, todo))
print("done", len(os.listdir(OUT)))
json.dump({"map": dict(todo), "missing": missing}, open(os.path.join(HERE, "forge_map.json"), "w"))
