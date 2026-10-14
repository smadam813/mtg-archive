"""Count Brawl legality facts in a Scryfall oracle_cards bulk file.

Usage: python count_brawl.py oracle-cards.jsonl.gz [default-cards.jsonl.gz]

Get the file URLs from https://api.scryfall.com/bulk-data. The script skips the
art_series and front_card layouts, as ingest does (ScryfallSnapshotReader), so
the counts match the live card table. With a Default Cards file it also counts
Arena availability and digital-only status over all printings of each oracle_id.
"""
import collections
import gzip
import json
import sys

EXCLUDED_LAYOUTS = {"art_series", "front_card"}
KEYS = ["commander", "brawl", "competitivebrawl", "standardbrawl", "standard", "historic", "alchemy"]


def stream(path):
    with gzip.open(path, "rt", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                yield json.loads(line)


def front(card):
    faces = card.get("card_faces") or []
    return faces[0] if faces and faces[0].get("type_line") else card


def is_meld_result(card):
    return card.get("layout") == "meld" and any(
        p.get("component") == "meld_result" and p.get("name") == card["name"] for p in card.get("all_parts") or [])


def brawl_commander_kind(card):
    """Arena rule: a legendary creature or planeswalker, a legendary Spacecraft
    or Vehicle with power and toughness (patch 2025.50.00), or a card that says
    it can be your commander. A meld result is a back face, never a commander.
    Returns the first kind that fits, or None."""
    if is_meld_result(card):
        return None
    face = front(card)
    t = face.get("type_line", "")
    legendary = "Legendary" in t
    if legendary and "Creature" in t:
        return "legendary creature"
    if legendary and "Planeswalker" in t:
        return "legendary planeswalker"
    has_pt = face.get("power") is not None
    if legendary and "Spacecraft" in t and has_pt:
        return "legendary Spacecraft with P/T"
    if legendary and "Vehicle" in t and has_pt:
        return "legendary Vehicle with P/T"
    texts = [card.get("oracle_text") or ""] + [f.get("oracle_text") or "" for f in card.get("card_faces") or []]
    if any("can be your commander" in text for text in texts):
        return "says it can be your commander"
    return None


def main():
    status = {k: collections.Counter() for k in KEYS}
    pair = collections.Counter()
    kinds = {"brawl": collections.Counter(), "competitivebrawl": collections.Counter()}
    a_names = collections.Counter()
    cards = {}
    for card in stream(sys.argv[1]):
        if card.get("layout") in EXCLUDED_LAYOUTS:
            continue
        cards[card["oracle_id"]] = card
        legal = card["legalities"]
        for k in KEYS:
            status[k][legal.get(k, "absent")] += 1
        pair[(legal["brawl"], legal["competitivebrawl"])] += 1
        for k in kinds:
            if legal[k] == "legal":
                kinds[k][brawl_commander_kind(card) or "not a commander"] += 1
        if card["name"].startswith("A-"):
            a_names[(card["name"], card["set_type"], "digital" if card["digital"] else "paper")] += 1

    print(f"oracle cards (ingest layouts): {len(cards)}")
    for k in KEYS:
        print(f"legalities.{k}: {dict(status[k].most_common())}")
    print("(brawl, competitivebrawl) pairs:", dict(pair.most_common()))
    games = [c.get("games") or [] for c in cards.values()]
    print("oracle print: digital", sum(c["digital"] for c in cards.values()),
          "| games has arena", sum("arena" in g for g in games),
          "| games == [arena]", sum(g == ["arena"] for g in games),
          "| arena_id set", sum(c.get("arena_id") is not None for c in cards.values()))
    print("names starting with A-:", dict(a_names))
    for k, counter in kinds.items():
        print(f"{k}-legal cards by commander kind:", dict(counter.most_common()))

    if len(sys.argv) > 2:
        prints = collections.defaultdict(lambda: [0, 0, 0])  # printings, digital printings, Arena printings
        for card in stream(sys.argv[2]):
            oid = card.get("oracle_id") or (card.get("card_faces") or [{}])[0].get("oracle_id")
            p = prints[oid]
            p[0] += 1
            p[1] += card.get("digital", False)
            p[2] += "arena" in (card.get("games") or [])
        print("default_cards: every printing digital",
              sum(1 for o in cards if prints[o][0] and prints[o][0] == prints[o][1]),
              "| any Arena printing", sum(1 for o in cards if prints[o][2]),
              "| oracle print games disagrees with any-printing Arena",
              sum(1 for o, c in cards.items() if bool(prints[o][2]) != ("arena" in (c.get("games") or []))))
        print("brawl legal or banned but no Arena printing",
              sum(1 for o, c in cards.items() if c["legalities"]["brawl"] != "not_legal" and not prints[o][2]),
              "| Arena printing but brawl not_legal",
              sum(1 for o, c in cards.items() if c["legalities"]["brawl"] == "not_legal" and prints[o][2]))


if __name__ == "__main__":
    main()
