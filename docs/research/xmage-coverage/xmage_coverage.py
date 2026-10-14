"""Check XMage card coverage of the mechanics-coverage-audit sample.

Inputs, in this folder:
  card-coverage.csv  the audit's per-card table (slots, basic flag, Forge status), copied unchanged
  sample-cards.csv   layout and front-face type line per card (dump_sample_cards.py)
Usage: python xmage_coverage.py <mage_checkout> [<sample.json>]
  <mage_checkout> is magefree/mage at xmage_1.4.61V1; only Mage.Sets/src/mage is read.
  <sample.json> is the audit sample; with it, the script names the deck of each missing card.
Writes xmage-coverage.csv and prints every number in the findings note.
"""

import csv
import json
import os
import re
import sys
import unicodedata
from collections import Counter, defaultdict

HERE = sys.path[0]
SRC = os.path.join(sys.argv[1], "Mage.Sets", "src", "mage")

SET_CARD = re.compile(r'^\s*cards\.add\(new SetCardInfo\("((?:[^"\\]|\\.)*)",.*?mage\.cards\.(\w+)\.(\w+)\.class', re.M)
COMMENTED_CARD = re.compile(r'^\s*//\s*cards\.add\(new SetCardInfo\("((?:[^"\\]|\\.)*)"', re.M)
UNFINISHED = re.compile(r"unfinished\s*=\s*Arrays\.asList\(([^;]*)\);", re.S)
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
TODO = re.compile(r"\bTODO\b.*")
NOT_IMPLEMENTED = re.compile(r"not (yet )?implemented|unimplemented", re.I)
RULES_CODE = re.compile(r"addAbility|getSpellAbility\(\)|getLeftHalfCard\(\)|getRightHalfCard\(\)|getSpellCard\(\)|"
                        r"getMainCard\(\)|Effect\b|Ability\(|addSuperType|secondSideCardClazz|meldsWith")


def norm(name):
    """Case, accents, and ligatures do not tell two card names apart."""
    name = name.replace("Æ", "Ae").replace("æ", "ae")
    name = unicodedata.normalize("NFKD", name)
    return "".join(ch for ch in name if not unicodedata.combining(ch)).casefold().strip()


def with_front(key):
    """A card matches by its full name or its front-face name, the rule of the Forge column."""
    return [key, key.split(" // ")[0]] if " // " in key else [key]


def load_xmage():
    registered = defaultdict(set)  # normalized name -> {class path}
    unfinished = {}  # normalized name -> set file whose `unfinished` list holds it back
    commented = {}  # normalized name -> set file with a commented-out registration
    for fn in sorted(os.listdir(os.path.join(SRC, "sets"))):
        raw = open(os.path.join(SRC, "sets", fn), encoding="utf-8", errors="replace").read()
        for name in COMMENTED_CARD.findall(raw):
            for k in with_front(norm(name)):
                commented.setdefault(k, fn)
        body = BLOCK_COMMENT.sub("", raw)
        m = UNFINISHED.search(body)
        held = set()
        if m and "unfinished.contains" in body:
            held = {norm(n) for n in re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))}
            for n in held:
                for k in with_front(n):
                    unfinished.setdefault(k, fn)
        for name, package, cls in SET_CARD.findall(body):
            key = norm(name.replace('\\"', '"'))
            if key in held:
                continue
            path = os.path.join(SRC, "cards", package, cls + ".java")
            for k in with_front(key):
                registered[k].add(path)
    return registered, unfinished, commented


def class_markers(path):
    body = open(path, encoding="utf-8", errors="replace").read()
    code = "\n".join(l for l in body.split("\n") if not l.lstrip().startswith(("import ", "package ")))
    return {"todos": [t.strip() for t in TODO.findall(body)],
            "not_implemented": [l.strip() for l in body.split("\n") if NOT_IMPLEMENTED.search(l)],
            "rules_code": bool(RULES_CODE.search(code))}


def corpus_markers():
    n, todo, not_impl, no_code = 0, 0, 0, 0
    for root, _, files in os.walk(os.path.join(SRC, "cards")):
        for fn in files:
            mk = class_markers(os.path.join(root, fn))
            n += 1
            todo += bool(mk["todos"])
            not_impl += bool(mk["not_implemented"])
            no_code += not mk["rules_code"]
    return n, todo, not_impl, no_code


def main():
    registered, unfinished, commented = load_xmage()
    meta = {r["card"]: r for r in csv.DictReader(open(os.path.join(HERE, "sample-cards.csv"), encoding="utf-8"))}
    audit = list(csv.DictReader(open(os.path.join(HERE, "card-coverage.csv"), encoding="utf-8")))

    rows = []
    for a in audit:
        name, m = a["card"], meta[a["card"]]
        keys = with_front(norm(name))
        hit = next((registered[k] for k in keys if k in registered), None)
        mk = {"todos": [], "not_implemented": [], "rules_code": True}
        for p in sorted(hit or []):
            if not os.path.exists(p):  # basic lands: mage.cards.basiclands lives outside Mage.Sets
                continue
            one = class_markers(p)
            mk["todos"] += one["todos"]
            mk["not_implemented"] += one["not_implemented"]
            mk["rules_code"] = mk["rules_code"] and one["rules_code"]
        held = next((unfinished[k] for k in keys if k in unfinished), "")
        cut = next((commented[k] for k in keys if k in commented), "")
        if hit and not mk["rules_code"] and m["has_text"] == "True":
            status = "stub"
        elif hit:
            status = "present"
        elif held:
            status = "unfinished"
        elif cut:
            status = "commented_out"
        else:
            status = "missing"
        rows.append({
            "card": name, "slots": int(a["slots"]), "decks": int(a["decks"]), "basic": a["basic"] == "True",
            "land": "Land" in m["front_type_line"], "layout": m["layout"], "forge": a["forge"], "xmage": status,
            "xmage_class": ";".join(os.path.relpath(p, SRC).replace(os.sep, "/") for p in sorted(hit or [])),
            "held_back_in": held or cut, "todo": " | ".join(mk["todos"]),
            "not_implemented": " | ".join(mk["not_implemented"]),
        })

    with open(os.path.join(HERE, "xmage-coverage.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.DictWriter(f, fieldnames=list(rows[0]))
        wr.writeheader()
        wr.writerows(rows)

    def share(rs, ok):
        n = sum(r["slots"] for r in rs)
        k = sum(r["slots"] for r in rs if ok(r))
        return f"{k}/{n} ({100 * k / n:.1f}%)"

    present = lambda r: r["xmage"] == "present"
    clean = lambda r: present(r) and not r["todo"] and not r["not_implemented"]
    forge = lambda r: r["forge"] == "present"
    nb = [r for r in rows if not r["basic"]]
    groups = {"all slots": rows, "nonbasic slots": nb,
              "nonbasic lands": [r for r in nb if r["land"]], "nonland slots": [r for r in nb if not r["land"]]}
    n, todo, not_impl, no_code = corpus_markers()
    print(f"xmage corpus: {n} card classes, {len(registered)} registered name keys, "
          f"{len(unfinished)} unfinished name keys, {len(commented)} commented-out name keys")
    print(f"  classes with a TODO note {todo}, with a 'not implemented' note {not_impl}, without rules code {no_code}")
    print("sample unique cards", len(rows), Counter(r["xmage"] for r in rows))
    for g, rs in groups.items():
        print(f"{g}: xmage {share(rs, present)}, xmage without a TODO or 'not implemented' note {share(rs, clean)}, "
              f"forge {share(rs, forge)}")
    print(f"unique nonbasic cards: xmage {sum(1 for r in nb if present(r))}/{len(nb)}, "
          f"with a TODO note {sum(1 for r in nb if present(r) and r['todo'])}, "
          f"with a 'not implemented' note {sum(1 for r in nb if present(r) and r['not_implemented'])}")
    multi = [r for r in rows if r["layout"] != "normal"]
    print(f"multi-face and special layouts: {sum(1 for r in multi if present(r))}/{len(multi)} present,",
          dict(Counter(r["layout"] for r in multi)))
    print("\nnot present:")
    for r in sorted((r for r in rows if not present(r)), key=lambda r: (-r["slots"], r["card"])):
        print(f"  {r['card']}: {r['xmage']} {r['held_back_in']} slots {r['slots']} decks {r['decks']} "
              f"{'land' if r['land'] else 'nonland'} {r['layout']}")
    if len(sys.argv) > 2:
        gone = {k: r["card"] for r in rows if not present(r) for k in with_front(norm(r["card"]))}
        for d in json.load(open(sys.argv[2], encoding="utf-8"))["decks"]:
            for entry in d["cards"]:
                k = norm(entry[0])
                if k in gone or norm(entry[0].split(" // ")[0]) in gone:
                    print(f"  in deck {d['source']}/{d['id']} ({d['name']}): {entry[0]}")
    print("\npresent with a TODO or 'not implemented' note (by slots):")
    for r in sorted((r for r in rows if present(r) and (r["todo"] or r["not_implemented"])),
                    key=lambda r: (-r["slots"], r["card"])):
        print(f"  {r['card']} [{r['slots']}] {r['xmage_class']}: {r['todo']} {r['not_implemented']}")


if __name__ == "__main__":
    main()
