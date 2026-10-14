"""Measure XMage card coverage of the Arena Brawl card pools.

Usage: python brawl_coverage.py <mage_checkout> <oracle-cards.jsonl.gz> <default-cards.jsonl.gz>
  <mage_checkout>  magefree/mage at xmage_1.4.61V1; only Mage.Sets/src/mage is read.
  oracle-cards     Scryfall oracle_cards bulk file: one object per Oracle ID, with legalities.
  default-cards    Scryfall default_cards bulk file: every printing, to tell digital-only cards
                   (no printing with "paper" in games) and the first Arena printing's set.
Writes missing-cards.csv beside this script and prints every number in the findings note.

XMage side: the rule of docs/research/xmage-coverage/xmage_coverage.py. A card counts when an
active SetCardInfo line registers it and no `unfinished` list holds it back. Names match by the
full name or the front-face name, ignoring case and accents; a second pass also ignores spaces and
punctuation, and the script lists each card that only it matches. Scryfall held no `A-` card on
2026-10-03, so the "rebalanced" rows print 0/0; the code keeps them for a later snapshot.
"""

import csv
import gzip
import json
import os
import re
import sys
import unicodedata
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))

SET_CARD = re.compile(r'^\s*cards\.add\(new SetCardInfo\("((?:[^"\\]|\\.)*)",.*?mage\.cards\.(\w+)\.(\w+)\.class', re.M)
SET_HEADER = re.compile(r'super\("((?:[^"\\]|\\.)*)",\s*"(\w+)"')
UNFINISHED = re.compile(r"unfinished\s*=\s*Arrays\.asList\(([^;]*)\);", re.S)
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT = re.compile(r"^\s*//.*$", re.M)

FORMATS = ("brawl", "standardbrawl", "competitivebrawl")
TAG_DATE = "2026-08-12"  # commit date of xmage_1.4.61V1 (105d560e)

# Digital-only mechanics, found in the Oracle text of each face.
MECHANICS = {
    "seek": re.compile(r"\bseeks?\b", re.I),
    "perpetual": re.compile(r"\bperpetually\b", re.I),
    "conjure": re.compile(r"\bconjures?\b", re.I),
    "spellbook": re.compile(r"\bspellbook\b", re.I),
    "specialize": re.compile(r"\bspecializes?\b", re.I),
    "boon": re.compile(r"\bboon with\b", re.I),
    "intensity": re.compile(r"\bintensity\b", re.I),
}


def oracle_text(card):
    faces = card.get("card_faces") or [card]
    return "\n".join(f.get("oracle_text", "") for f in faces)


def norm(name):
    """Case, accents, and ligatures do not tell two card names apart."""
    name = name.replace("Æ", "Ae").replace("æ", "ae")
    name = unicodedata.normalize("NFKD", name)
    return "".join(ch for ch in name if not unicodedata.combining(ch)).casefold().strip()


def with_front(key):
    return [key, key.split(" // ")[0]] if " // " in key else [key]


def loose(key):
    """Second-pass key without spaces and punctuation: XMage writes "With Great Power...", Scryfall
    "With Great Power . . .". The script lists every card that matches only on this key."""
    return re.sub(r"[^0-9a-z]", "", key)


def load_xmage(checkout):
    """Normalized name -> set codes of the XMage set classes that register it."""
    sets_dir = os.path.join(checkout, "Mage.Sets", "src", "mage", "sets")
    registered = defaultdict(set)
    held_back = {}
    for fn in sorted(os.listdir(sets_dir)):
        raw = open(os.path.join(sets_dir, fn), encoding="utf-8", errors="replace").read()
        body = LINE_COMMENT.sub("", BLOCK_COMMENT.sub("", raw))
        header = SET_HEADER.search(body)
        code = header.group(2) if header else fn
        held = set()
        m = UNFINISHED.search(body)
        if m and "unfinished.contains" in body:
            held = {norm(n) for n in re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))}
            for n in held:
                held_back.setdefault(n, code)
        for name, _package, _cls in SET_CARD.findall(body):
            key = norm(name.replace('\\"', '"'))
            if key in held:
                continue
            for k in with_front(key):
                registered[k].add(code)
    return registered, held_back


def read_jsonl(path):
    with gzip.open(path, "rt", encoding="utf-8") as f:
        for line in f:
            line = line.strip().rstrip(",")
            if line and line not in ("[", "]"):
                yield json.loads(line)


def load_printings(path):
    """Oracle ID -> has a paper printing; -> (date, set code, set name) of the first Arena printing;
    -> date of the first printing of any kind."""
    paper = defaultdict(bool)
    first_arena = {}
    first_print = {}
    for c in read_jsonl(path):
        oid = c.get("oracle_id") or (c.get("card_faces") or [{}])[0].get("oracle_id")
        if not oid:
            continue
        games = c.get("games", [])
        paper[oid] |= "paper" in games
        date = c.get("released_at", "9999-99-99")
        first_print[oid] = min(first_print.get(oid, date), date)
        if "arena" in games:
            key = (date, c["set"].upper(), c.get("set_name", ""))
            if oid not in first_arena or key < first_arena[oid]:
                first_arena[oid] = key
    return paper, first_arena, first_print


def is_meld_back(card):
    return card.get("layout") == "meld" and any(
        p.get("component") == "meld_result" and p.get("id") == card["id"] for p in card.get("all_parts", []))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    checkout, oracle_path, default_path = sys.argv[1:4]
    registered, held_back = load_xmage(checkout)
    registered_loose = defaultdict(set)
    for k, codes in registered.items():
        registered_loose[loose(k)] |= codes
    loose_only = []
    paper, first_arena, first_print = load_printings(default_path)

    rows = []
    meld_backs = Counter()
    for c in read_jsonl(oracle_path):
        legal = {f: c.get("legalities", {}).get(f, "not_legal") for f in FORMATS}
        if all(v == "not_legal" for v in legal.values()):
            continue
        if is_meld_back(c):  # the melded back face plays only through its two front cards
            meld_backs.update(f for f in FORMATS if legal[f] != "not_legal")
            continue
        oid = c["oracle_id"]
        keys = with_front(norm(c["name"]))
        hit = next((k for k in keys if k in registered), None)
        hit_sets = registered[hit] if hit else set()
        if not hit:
            lk = next((loose(k) for k in keys if loose(k) in registered_loose), None)
            if lk:
                hit, hit_sets = lk, registered_loose[lk]
                loose_only.append(c["name"])
        held = next((held_back[k] for k in keys if k in held_back), "")
        arena = first_arena.get(oid, (c.get("released_at", ""), c["set"].upper(), c.get("set_name", "")))
        a_name = c["name"].startswith("A-")
        base_hit = False
        if a_name:
            base = c["name"][2:].replace(" // A-", " // ")
            base_hit = any(k in registered for k in with_front(norm(base)))
        rows.append({
            "name": c["name"], "layout": c["layout"],
            "brawl": legal["brawl"], "standardbrawl": legal["standardbrawl"],
            "competitivebrawl": legal["competitivebrawl"],
            "kind": "rebalanced" if a_name else ("digital" if not paper.get(oid, False) else "paper"),
            "xmage": "present" if hit else ("unfinished" if held else "missing"),
            "xmage_sets": ";".join(sorted(hit_sets)) if hit else held,
            "original_in_xmage": base_hit if a_name else "",
            "arena_set": arena[1], "arena_set_name": arena[2], "arena_date": arena[0],
            "first_printed": first_print.get(oid, c.get("released_at", "")),
            "mechanics": ";".join(m for m, rx in MECHANICS.items() if rx.search(oracle_text(c))),
        })

    def share(rs):
        n = len(rs)
        k = sum(1 for r in rs if r["xmage"] == "present")
        return f"{k}/{n} ({100 * k / n:.1f}%)" if n else "0/0"

    print(f"xmage registered name keys: {len(registered)}, held back by `unfinished`: {len(held_back)}")
    print(f"meld back faces left out: {dict(meld_backs)}")
    print(f"matched only without spaces and punctuation ({len(loose_only)}): {sorted(loose_only)}")
    for f in FORMATS:
        for status in ("legal", "banned"):
            rs = [r for r in rows if r[f] == status]
            if not rs:
                continue
            print(f"\n{f} {status}: {len(rs)} cards, xmage {share(rs)}")
            for kind in ("paper", "digital", "rebalanced"):
                print(f"  {kind}: {share([r for r in rs if r['kind'] == kind])}")
            old = [r for r in rs if r["first_printed"] <= TAG_DATE]
            print(f"  first printed on or before the tag date {TAG_DATE}: {share(old)}; "
                  f"paper {share([r for r in old if r['kind'] == 'paper'])}; "
                  f"first printed after it: {share([r for r in rs if r['first_printed'] > TAG_DATE])}")
            reb = [r for r in rs if r["kind"] == "rebalanced"]
            if reb:
                print(f"  rebalanced whose original name XMage has: {sum(1 for r in reb if r['original_in_xmage'])}/{len(reb)}")
            print("  layouts not present:", dict(Counter(r["layout"] for r in rs if r["xmage"] != "present")))
            print("  unfinished:", sum(1 for r in rs if r["xmage"] == "unfinished"))
            dig = [r for r in rs if r["kind"] == "digital"]
            if dig:
                print("  digital-only cards with no digital-only mechanic:",
                      share([r for r in dig if not r["mechanics"]]))
                for m in MECHANICS:
                    print(f"    {m}: {share([r for r in dig if m in r['mechanics'].split(';')])}")
                print("  digital-only cards XMage has, by first Arena set:",
                      dict(Counter(r["arena_set"] for r in dig if r["xmage"] == "present")))

    for f in ("brawl", "standardbrawl"):
        legal_rows = [r for r in rows if r[f] == "legal"]
        miss = [r for r in legal_rows if r["xmage"] != "present"]
        print(f"\n{f} legal, not in XMage, by first Arena set ({len(miss)} cards):")
        by_set = Counter((r["arena_date"][:10], r["arena_set"], r["arena_set_name"]) for r in miss)
        in_set = Counter(r["arena_set"] for r in legal_rows)
        kinds = defaultdict(Counter)
        for r in miss:
            kinds[r["arena_set"]][r["kind"]] += 1
        for (date, code, name), n in sorted(by_set.items(), key=lambda kv: (-kv[1], kv[0])):
            print(f"  {code:6} {date} {name}: {n} of {in_set[code]} missing {dict(kinds[code])}")
        paper_miss = [r for r in miss if r["kind"] == "paper"]
        print(f"  paper cards missing ({len(paper_miss)}):")
        for r in sorted(paper_miss, key=lambda r: (r["arena_date"], r["name"])):
            print(f"    {r['name']} [{r['arena_set']}] {r['xmage']} {r['xmage_sets']}")

    out = [r for r in rows if r["xmage"] != "present" and (r["brawl"] == "legal" or r["standardbrawl"] == "legal")]
    out.sort(key=lambda r: (r["arena_date"], r["arena_set"], r["name"]))
    with open(os.path.join(HERE, "missing-cards.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.DictWriter(f, fieldnames=["name", "kind", "brawl", "standardbrawl", "competitivebrawl", "layout",
                                           "xmage", "mechanics", "arena_set", "arena_set_name", "arena_date",
                                           "first_printed"])
        wr.writeheader()
        for r in out:
            wr.writerow({k: r[k] for k in wr.fieldnames})
    print(f"\nwrote missing-cards.csv: {len(out)} rows")


if __name__ == "__main__":
    main()
