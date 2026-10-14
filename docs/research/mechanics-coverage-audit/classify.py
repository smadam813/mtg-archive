"""Tag each card in the deck sample with the mechanics it needs, then score
coverage for our engine and for Forge.

Inputs (same directory): sample.json (fetch_sample.py), cards.json (archive
card dump), and a Forge checkout path. Output: results.json plus a printed
summary.

Usage: python classify.py <forge_cardsfolder>
"""

import json
import os
import re
import sys
from collections import Counter, defaultdict

HERE = sys.path[0]
BSN = chr(92) + "n"
NL = chr(10)
BASICS = {"Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes",
          "Snow-Covered Plains", "Snow-Covered Island", "Snow-Covered Swamp",
          "Snow-Covered Mountain", "Snow-Covered Forest", "Snow-Covered Wastes"}

# ---------------------------------------------------------------- tags
# Each mechanic tag belongs to one group. Subsystem tags name a rules
# subsystem; effect tags name an effect kind. None of these tags is covered
# by our engine: covered clauses are whitelisted separately below.
SUBSYSTEMS = {
    "stack": "Stack and responses",
    "combat": "Combat",
    "replacement": "Replacement and prevention effects",
    "layers": "Continuous effects (layers)",
    "copy": "Copy effects",
    "opponents": "Targets, opponents, and other players' objects",
    "triggers": "Trigger events beyond on_enter, at_step, on_draw",
    "graveyard": "Graveyard and exile as play zones",
    "counters": "Counters (+1/+1, loyalty, other)",
    "tokens": "Tokens",
    "life": "Life total (gain, loss, payment)",
}

EFFECTS = [
    # (tag, group, regex over normalized text)
    ("counter_spell", "stack", r"\bcounter (target|that|it|all|each)\b|\bcounter up to\b"),
    ("flash", "stack", r"\bflash\b"),
    ("split_second", "stack", r"\bsplit second\b"),
    ("copy_spell", "copy", r"\bcop(y|ies) (target|that|it|the next|each|this) (instant|sorcery|spell|activated|triggered|ability)|\bcopy (target|that) spell|\bstorm\b"),
    ("copy_permanent", "copy", r"\bcopy of\b|\bas a copy\b|\bbecomes? a copy\b|token that's a copy|\bcopies of\b"),
    ("combat_words", "combat", r"\battack|\bblock|\bcombat damage|\bcombat\b|\bcrew \d"),
    ("evasion_keywords", "combat", r"\b(flying|trample|menace|first strike|double strike|deathtouch|vigilance|reach|haste|lifelink|shadow|fear|intimidate|skulk)\b"),
    ("attach", "combat", r"\bequip\b|\bequipped\b|\benchant (creature|permanent|artifact|land|player)\b|\benchanted (creature|permanent)\b|\battach"),
    ("replacement_instead", "replacement", r"\binstead\b|\bwould\b"),
    ("prevention", "replacement", r"\bprevent"),
    ("enters_with", "replacement", r"enters (the battlefield )?with\b|\bas CARDNAME enters, (?!choose a card name)|\bskip\b"),
    ("etb_tapped", "replacement", r"enters (the battlefield )?tapped"),
    ("pump", "layers", r"\bgets? [+-][\dX]+/[+-][\dX]+|\bget [+-][\dX]+/[+-][\dX]+"),
    ("grant_ability", "layers", r"\b(have|has|gain|gains) (flying|first strike|double strike|deathtouch|haste|hexproof|indestructible|lifelink|menace|reach|trample|vigilance|ward|protection|shroud|myriad|exalted|defender|infect|wither|toxic|annihilator|prowess|undying|persist|flash|cascade|convoke|all activated abilities|\")"),
    ("type_change", "layers", r"\bbecomes? (a|an|the|\d|all)\b|in addition to (its|their) other (\w+ )?types|\bare (mountains|islands|swamps|forests|plains)\b|\bearthbend|\blose(s)? all abilities|\bbase power|power and toughness (are|is) each equal|\bis every creature type|\bare (\w+ )?(creatures|artifacts|lands) in addition"),
    ("gain_control", "layers", r"\bgain control\b|\bexchange control\b"),
    ("protection", "layers", r"\bhexproof\b|\bindestructible\b|\bprotection from\b|\bshroud\b|\bward\b|\bphases? out\b"),
    ("targets_other", "opponents", r"\btarget (creature|permanent|artifact|enchantment|planeswalker|nonland|land|spell|opponent|player|attacking|blocking|tapped|nontoken|noncreature|nonartifact|nonblack|nonwhite|legendary|creature or|artifact or)|\bany target\b|\bup to (one|two|three|\w+) target\b"),
    ("each_player", "opponents", r"\bopponents?\b|\beach player\b|\beach other player\b|\ball players\b|\bplayers can't\b|\bthat player\b"),
    ("board_wipe", "opponents", r"\b(destroy|exile) (all|each)\b|\breturn (all|each) .{0,40}to (its|their) owners?'?s? hand|\bdeals? (\d+|X) damage to each\b|\beach creature\b.{0,40}\b(dies|destroy|sacrifice)|\ball creatures get -"),
    ("removal", "opponents", r"\b(destroy|exile) target\b|\breturn target .{0,60}to (its|their) owner'?s'? hand|\bsacrifices? (a|an|two) (creature|permanent|artifact|enchantment)"),
    ("damage", "life", r"\bdeals?\b[^.]{0,40}\bdamage"),
    ("life_gain", "life", r"\bgain(s)? (\d+|X|that much|life)\b|\blifelink\b|\bgains? .{0,20}life\b"),
    ("life_loss", "life", r"\blose(s)? (\d+|X|that much|life|half)|\blife total\b"),
    ("pay_life", "life", r"\bpay (\d+|X) life\b|\bpay life\b"),
    ("token", "tokens", r"\btokens?\b|\binvestigate\b|\bamass\b|\bpopulate\b|\bincubate\b|\bembalm\b|\beternalize\b"),
    ("plus_counters", "counters", r"\+1/\+1 counters?|\bproliferate\b"),
    ("other_counters", "counters", r"\b(?!counter target)\w+ counters?\b(?! target)|\bproliferate\b|\bblight\b"),
    ("loyalty", "counters", r"\bloyalty\b|^\[[+\-−]"),
    ("recursion", "graveyard", r"\bfrom (your|a|their|an opponent's) graveyards?\b|\bgraveyard to\b|\bfrom your graveyard\b"),
    ("graveyard_other", "graveyard", r"\bgraveyards?\b|\bflashback\b|\bescape\b|\bunearth\b|\bdisturb\b|\bretrace\b|\bjump-start\b|\bdelve\b|\bdredge\b"),
    ("exile_zone", "graveyard", r"\bexile the top\b|\bfrom exile\b|\bexiled (card|cards|with)\b|\bforetell\b|\bplotted\b|\bplot\b|\bsuspend\b|\badventure\b|\bcascade\b|\bdiscover\b"),
    ("cast_from_library", "graveyard", r"(cast|play) [^.]{0,60}from the top of your library|\bplay with the top card|\blook at the top card of your library any time"),
    ("move_self", None, r"\bput CARDNAME (on top of|on the bottom of|into) (its owner's|your) (library|hand)|\breturn CARDNAME (from your graveyard )?to (its owner's|your) hand"),
    ("command_zone", None, r"\bcommand zone\b|\bcommander tax\b"),
    ("activation_condition", None, r"\bactivate (this ability )?only (if|during|once|while)\b"),
    ("trigger_doubling", "replacement", r"triggers? an additional time"),
    ("tokens_kw", "tokens", None),
    ("instant_interaction", "stack", None),  # set in code
    ("keyword_other", None, None),
    ("cast_free", "graveyard", r"without paying (its|their) mana costs?\b|\byou may (cast|play) (it|them|that card|those cards|the exiled|cards exiled)"),
    ("discard", None, r"\bdiscards?\b|\bcycling\b|\bmadness\b"),
    ("mill", None, r"\bmill\b|\bmills\b|put the top .{0,30} into (your|their|his or her) graveyard"),
    ("surveil", None, r"\bsurveil\b"),
    ("sacrifice_other", None, r"\bsacrifices?\b"),
    ("extra_turn_combat", None, r"\bextra turn\b|\badditional combat\b"),
    ("cost_reduction", None, r"\bcosts? (\{\d+\}|\{X\}|\w+) less\b|\bcost .{0,40}less to (cast|activate)|\bconvoke\b|\bimprovise\b|\baffinity\b|\bundaunted\b"),
    ("cost_increase", None, r"\bcosts? (\{\d+\}|\w+) more\b|\bcost .{0,40}more to (cast|activate)"),
    ("rule_restriction", None, r"\bcan't\b|\bdon't untap\b|\bdoesn't untap\b|\bno maximum hand size\b|\bmaximum hand size\b"),
    ("untap", None, r"\buntap (target|all|up to|each|another|CARDNAME|it|those|that|two|three|X|them|any)\b|\buntaps?\b"),
    ("blink", None, r"\bexile .{0,60}(then |, )return (it|that card|them|those cards|the exiled)|\breturn (it|that card|them|those cards|the exiled card) to the battlefield"),
    ("put_onto_battlefield", None, r"\bput (a|an|up to|that|those|any number|one|two|it|them|target|X|each)\b.{0,80}\bonto the battlefield\b"),
    ("extra_land", None, r"\bplay an additional land|\bplay (\w+ )?additional lands?|\byou may play lands from|\bplay lands from"),
    ("dig", None, r"\blook at the top (\w+|X) cards? of your library.{0,120}\b(put|reveal|exile)(?! them back)|\breveal the top (\w+|X) cards?\b"),
    ("reveal_until", None, r"\buntil (you|they|a player) (reveal|exile|mill)|\bcascade\b|\bdiscover\b"),
    ("modal", None, r"\bchoose (one|two|three|one or more|any number|up to)\b|\bescalate\b|\bspree\b"),
    ("alt_cost", None, r"\brather than pay|\bkicker\b|\bmultikicker\b|\bbuyback\b|\bevoke\b|\bemerge\b|\bdash\b|\bninjutsu\b|\boverload\b|\bmiracle\b|\bentwine\b|\bbestow\b|\bprototype\b|\bblitz\b|\bcasualty\b|\bofferings?\b"),
    ("random", None, r"\bat random\b|\bflip a coin\b|\broll (a|two|\w+) (d\d+|dice|die)\b"),
    ("transform", None, r"\btransforms?\b|\bdaybound\b|\bnightbound\b|\bmeld\b"),
    ("saga_class", None, r"\blore counter\b|\bchapter\b|\blevel \d|\bclass\b"),
    ("hand_size_draw_rule", None, r"\bdraw (an additional|additional) card|\bfor each card drawn"),
    ("mana_complex", None, r"\badd (an additional|one additional|x|that much|an amount)|\badd .{0,40} for each\b|\bspend this mana only\b|\bdon't lose this mana\b|\bmana of any type\b|\bdoubl(e|es|ing)\b|\btriple\b|\badd one mana of any color\b|\badd .{0,20}in any combination\b|\bmana of the chosen color\b|\bmana of any one color\b"),
    ("tutor_complex", None, None),   # set in code
    ("draw_complex", None, None),    # set in code
    ("cost_other", None, None),      # set in code
    ("trig_other", "triggers", None),  # set in code
    ("creature_body", "combat", None),  # set in code
    ("planeswalker", "counters", None),  # set in code
    ("x_cost", None, None),          # set in code
    ("unclassified", None, None),    # set in code
]
EFFECT_RE = {t: re.compile(r, re.I) for t, _, r in EFFECTS if r}
GROUP = {t: g for t, g, _ in EFFECTS}

COMBAT_KW = {"flying", "trample", "menace", "first strike", "double strike", "deathtouch", "vigilance",
             "reach", "haste", "lifelink", "defender", "exalted", "myriad", "melee", "battle cry",
             "shadow", "fear", "intimidate", "skulk", "horsemanship", "flanking", "bushido", "rampage",
             "annihilator", "afflict", "training", "mentor", "dethrone", "provoke", "banding", "toxic",
             "infect", "wither", "firebending", "islandwalk", "swampwalk", "forestwalk", "mountainwalk",
             "plainswalk", "soulbond", "squad", "backup", "mobilize", "devour", "devour land", "ascend",
             "station", "tiered", "bargain", "compleated", "cumulative upkeep", "vanishing", "echo",
             "warp", "mayhem", "prowl", "job select", "hideaway", "transmute", "rebound", "fuse"}
# Keywords listed above as combat are grouped by their main need below.
COMBAT_KW_TAG = {"firebending": "trig_attack", "soulbond": "layers_kw", "squad": "tokens_kw", "backup": "plus_counters",
                 "mobilize": "tokens_kw", "devour": "sacrifice_other", "devour land": "sacrifice_other",
                 "ascend": "keyword_other", "station": "other_counters", "tiered": "alt_cost", "bargain": "alt_cost",
                 "compleated": "other_counters", "cumulative upkeep": "other_counters", "vanishing": "other_counters",
                 "echo": "alt_cost", "warp": "alt_cost", "mayhem": "discard", "prowl": "alt_cost",
                 "job select": "tokens_kw", "hideaway": "exile_zone", "transmute": "tutor_complex",
                 "rebound": "exile_zone", "fuse": "alt_cost"}
KW_TAG = {"flash": "flash", "hexproof": "protection", "indestructible": "protection", "shroud": "protection",
          "ward": "protection", "prowess": "trig_cast", "storm": "copy_spell", "cascade": "reveal_until",
          "flashback": "graveyard_other", "equip": "attach", "cycling": "discard", "convoke": "cost_reduction",
          "affinity for artifacts": "cost_reduction", "improvise": "cost_reduction", "delve": "graveyard_other",
          "changeling": "type_change", "devoid": "type_change", "undying": "trig_dies", "persist": "trig_dies",
          "evolve": "trig_enters_other", "extort": "trig_cast", "landfall": "trig_landfall", "split second": "split_second",
          "living weapon": "tokens_kw", "crew": "combat_words",
          "kicker": "alt_cost", "multikicker": "alt_cost", "buyback": "alt_cost", "entwine": "alt_cost", "escape": "graveyard_other",
          "unearth": "graveyard_other", "embalm": "tokens_kw", "eternalize": "tokens_kw", "dredge": "graveyard_other",
          "modular": "plus_counters", "riot": "enters_with", "fabricate": "plus_counters", "sunburst": "enters_with",
          "offspring": "tokens_kw", "encore": "tokens_kw", "blitz": "alt_cost", "dash": "alt_cost", "evoke": "alt_cost",
          "ninjutsu": "alt_cost", "commander ninjutsu": "alt_cost", "bestow": "alt_cost", "miracle": "alt_cost",
          "overload": "alt_cost", "emerge": "alt_cost", "madness": "discard", "retrace": "graveyard_other",
          "jump-start": "graveyard_other", "aftermath": "graveyard_other", "disturb": "graveyard_other",
          "foretell": "exile_zone", "suspend": "exile_zone", "plot": "exile_zone", "casualty": "copy_spell",
          "replicate": "copy_spell", "demonstrate": "copy_spell", "cipher": "exile_zone", "phasing": "protection",
          "protection from everything": "protection", "totem armor": "replacement_instead",
          "umbra armor": "replacement_instead", "partner": "deck_rule", "companion": "deck_rule",
          "choose a background": "deck_rule", "friends forever": "deck_rule", "doctor's companion": "deck_rule"}
UNKNOWN_KW = Counter()

TRIGGER_KINDS = [
    ("trig_dies", r"\b(dies|die|put into (a|your|an opponent's) graveyard|leaves the battlefield)\b"),
    ("trig_cast", r"\b(cast|casts|copy|copies)\b"),
    ("trig_attack", r"\b(attacks?|blocks?|combat damage|deals damage|becomes blocked)\b"),
    ("trig_landfall", r"\bland (enters|you control enters)|\blands? enter"),
    ("trig_enters_other", r"\benters?\b"),
    ("trig_life", r"\b(gain|gains|lose|loses) life\b"),
    ("trig_counters", r"\bcounters?\b"),
    ("trig_tokens", r"\btokens?\b"),
    ("trig_sacrifice", r"\bsacrifice"),
    ("trig_mana", r"\btapped for mana|\btap .* for mana"),
    ("trig_draw_other", r"\bdraws?\b"),
    ("trig_step_other", r"\bat the beginning of\b"),
]
for t, _ in TRIGGER_KINDS:
    GROUP[t] = "triggers"

# ------------------------------------------------ covered-clause whitelist
MANA = r"(\{[WUBRGC]\})+"
FIXED_ADD = rf"add {MANA}((, | or |, or ){MANA})*"
SIMPLE_FILTER = (r"(a|an|up to (one|two|three)|one|two|three) "
                 r"((basic|legendary|snow) )?"
                 r"((land|creature|artifact|enchantment|instant|sorcery|planeswalker|battle)"
                 r"( or (land|creature|artifact|enchantment|instant|sorcery|planeswalker|battle))? )?cards?")
SEARCH = (rf"search your library for {SIMPLE_FILTER}(, reveal (it|them|that card|those cards))?"
          r"(,| and)? (put (it|that card|them|those cards) (into your hand|onto the battlefield( tapped)?)"
          r"(, then shuffle| then shuffle)?|then shuffle and put (it|that card) on top( of your library)?)")
EFFECT_OK = [
    FIXED_ADD,
    r"draw (a|one|two|three|four|five|\d+) cards?",
    r"scry \d+",
    SEARCH,
    r"then shuffle",
    r"you may shuffle( your library)?",
    r"look at the top (one|two|three|four|five|\d+) cards of your library, then put them back in any order",
    r"(then )?put (a|one|two|three|\d+) cards? from your hand on top of your library( in any order)?",
    r"draw (a|one|two|three|\d+) cards?, then put (a|one|two|three|\d+) cards? from your hand on top of your library( in any order)?",
]
COST_OK = re.compile(r"^((\{[\dWUBRGC]\}|\{\d+\}|\{[WUBRG]/[WUBRG]\})+|\{T\}|sacrifice CARDNAME|exile CARDNAME from your hand)$", re.I)
EFFECT_OK_RE = re.compile(r"^(" + "|".join(f"(?:{e})" for e in EFFECT_OK) + r")$", re.I)
WHOLE_OK = [
    re.compile(r"^as CARDNAME enters, choose a card name$", re.I),
    re.compile(r"^activated abilities of sources with the chosen name can't be activated unless they're mana abilities$", re.I),
    re.compile(r"^\(?\{T\}: add one mana of any color\)?$", re.I),
]
INERT_MANA = re.compile(r"^\{T\}: add (\{[WUBRGC]\}( or \{[WUBRGC]\})*|one mana of any color|one mana of any color in your commander's color identity)$", re.I)
TRIGGER_OK = re.compile(r"^(when CARDNAME enters|at the beginning of (your|each) (upkeep|draw step|end step|precombat main phase)|whenever you draw (a|your first) card)$", re.I)


def normalize(card):
    names = set()
    faces = card.get("faces") or []
    texts, types = [], []
    if faces:
        for f in faces:
            texts.append(f.get("oracle_text") or "")
            types.append(f.get("type_line") or "")
            names.add(f.get("name") or "")
    else:
        texts.append(card.get("oracle_text") or "")
        types.append(card.get("type_line") or "")
    names.add(card["name"])
    for n in list(names):
        if "," in n:
            names.add(n.split(",")[0])
        if " // " in n:
            names.update(n.split(" // "))
    text = "\n".join(texts)
    text = re.sub(r"\([^)]*\)", "", text)
    # ability words and flavor words ("Landfall — ") carry no rules meaning
    text = re.sub(r"(?m)^[^\n{}:—.]{1,40} — (?=\S)", "", text)
    for n in sorted((n for n in names if n), key=len, reverse=True):
        text = text.replace(n, "CARDNAME")
    text = re.sub(r"\bthis (creature|artifact|land|enchantment|permanent|spell|card|planeswalker|Saga|Equipment|Aura|Vehicle|battle)\b", "CARDNAME", text, flags=re.I)
    text = text.replace("−", "-")
    return text, " // ".join(types)


def sentences(line):
    parts = re.split(r"(?<=[.])\s+(?=[A-Z{])", line.strip())
    return [p.strip().rstrip(".").strip() for p in parts if p.strip().rstrip(".").strip()]


def clause_ok(s):
    """True when our 8 primitives and closed costs and triggers express the clause."""
    if any(r.match(s) for r in WHOLE_OK):
        return True
    if EFFECT_OK_RE.match(s):
        return True
    m = re.match(r"^([^:\"]+): (.+)$", s)
    if m:
        costs = [c.strip() for c in m.group(1).split(",")]
        if all(COST_OK.match(c) for c in costs):
            return all(EFFECT_OK_RE.match(e.strip()) for e in re.split(r"\. ", m.group(2)))
        return False
    m = re.match(r"^(when[^,]+|at the beginning of [^,]+|whenever[^,]+), (you may )?(.+)$", s, re.I)
    if m and TRIGGER_OK.match(m.group(1).strip()):
        return bool(EFFECT_OK_RE.match(m.group(3).strip()))
    return False


def classify(card):
    text, types = normalize(card)
    tags = set()
    lines = [l for l in text.split("\n") if l.strip()]
    kw = set(k.lower() for k in card.get("keywords") or [])
    is_creature = "Creature" in types.split(" // ")[0] or (card.get("layout") in ("modal_dfc",) and "Creature" in types)
    is_pw = "Planeswalker" in types

    for tag, rx in EFFECT_RE.items():
        if rx.search(text):
            tags.add(tag)
    # keywords Scryfall lists but reminder-stripping may hide
    for k in kw:
        if k in ("flying", "trample", "menace", "first strike", "double strike", "deathtouch", "vigilance", "reach", "haste", "lifelink"):
            tags.add("evasion_keywords")
        if k in ("flash",):
            tags.add("flash")
        if k in ("flashback", "escape", "unearth", "disturb", "retrace", "jump-start", "delve", "dredge", "embalm", "eternalize", "aftermath"):
            tags.add("graveyard_other")
        if k in ("cascade", "discover"):
            tags.update({"reveal_until", "cast_free"})
        if k in ("convoke", "improvise", "affinity"):
            tags.add("cost_reduction")
        if k in ("cycling",) or k.endswith("cycling"):
            tags.add("discard")
        if k in ("equip",):
            tags.add("attach")
        if k in ("storm",):
            tags.add("copy_spell")
        if k in ("transform", "daybound", "nightbound"):
            tags.add("transform")
    for l in lines:
        if re.fullmatch(r"[A-Za-z ,\-—{}0-9']+", l) and not re.search(r"[:.]", l) and len(l) < 60 \
                and not re.match(r"(when|whenever|at the|choose)\b", l, re.I):
            for word in [w.strip().lower() for w in l.split(",")]:
                base = re.sub(r"[ —-]*(\{.*|\d+)$", "", word).strip()
                base = re.sub(r"^.*— ", "", base)
                if base.startswith("enchant") or base.startswith("equip"):
                    base = "equip"
                elif base.endswith("cycling"):
                    base = "cycling"
                elif base.startswith("protection from"):
                    base = "hexproof"
                elif base.startswith("partner"):
                    base = "partner"
                elif base.startswith("gift"):
                    tags.add("each_player")
                    continue
                if base in COMBAT_KW_TAG:
                    tag = COMBAT_KW_TAG[base]
                    tags.add("type_change" if tag == "layers_kw" else tag)
                elif base in COMBAT_KW:
                    tags.add("evasion_keywords")
                elif base in KW_TAG:
                    if KW_TAG[base] != "deck_rule":  # deck-building rules need no engine support
                        tags.add(KW_TAG[base])
                else:
                    tags.add("keyword_other")
                    UNKNOWN_KW[base] += 1
    # counter-spell text also matches other_counters' loose pattern; drop it
    if "other_counters" in tags and not re.search(r"\b(?!target\b)\w+ counters? (on|from)\b|\bcounters? on\b|\bproliferate\b|\bwith (a|an|\w+) .{0,20}counters?\b|\bput (a|an|\w+) .{0,20}counters?\b|\bremove .{0,20}counters?\b|\bblight\b", text, re.I):
        tags.discard("other_counters")
    # self-sacrifice as a cost is covered; only other sacrifices count
    stripped = re.sub(r"\bsacrifice CARDNAME\b", "", text, flags=re.I)
    if not re.search(r"\bsacrifices?\b", stripped, re.I):
        tags.discard("sacrifice_other")
    if re.search(r"\bsearch (your|their|target (player|opponent)'s|his or her|its controller's) library", text, re.I):
        for m in re.finditer(r"search [^.]*", text, re.I):
            if not re.match(SEARCH, m.group(0).split(". ")[0], re.I) and not re.match(rf"search your library for {SIMPLE_FILTER}", m.group(0), re.I):
                tags.add("tutor_complex")
            elif re.search(r"search your library for [^,.]*(with|named|that|mana value|power|toughness|subtype|Forest|Island|Swamp|Mountain|Plains|Aura|Equipment|Vehicle|Dragon|Elf|Goblin|Human|Vampire|Zombie|Wizard|Sliver|Dinosaur)", m.group(0), re.I):
                tags.add("tutor_complex")
    if re.search(r"\bdraws? (x|cards equal|that many|a card for each|an additional)|\b(each player|target player|that player|each opponent|an opponent) draws\b", text, re.I):
        tags.add("draw_complex")
    if re.search(r"\{X\}", (card.get("mana_cost") or "") + text):
        tags.add("x_cost")
    if ("Instant" in types or "flash" in tags) and tags & {"targets_other", "removal", "counter_spell", "protection", "grant_ability", "pump"}:
        tags.add("instant_interaction")
    if is_pw:
        tags.add("planeswalker")
    if is_creature:
        # a creature that only makes mana still has a body, but combat is moot
        body = True  # every creature can attack or block, power 0 included
        mana_only = bool(lines) and all(INERT_MANA.match(s) for l in lines for s in sentences(l) if s and not re.fullmatch(r"[\w ,]+", s))
        if body and not mana_only:
            tags.add("creature_body")
    # activated-ability costs outside the closed set
    for l in lines:
        for s in sentences(l):
            m = re.match(r"^([^:\"]+): ", s)
            if m and ("{" in m.group(1) or re.match(r"^(sacrifice|pay|discard|exile|remove|tap|return|\[)", m.group(1), re.I)):
                costs = [c.strip() for c in m.group(1).split(",")]
                if not all(COST_OK.match(c) or re.fullmatch(r"\{[\dWUBRGCX/P]+\}", c) for c in costs):
                    tags.add("cost_other")
            if m and re.match(r"^\[", s):
                tags.add("loyalty")
    # triggers
    for l in lines:
        for s in sentences(l):
            m = re.match(r"^(when[^,]+|whenever[^,]+|at the beginning of [^,]+),", s, re.I)
            if not m:
                continue
            head = m.group(1)
            if TRIGGER_OK.match(head.strip()):
                continue
            for t, rx in TRIGGER_KINDS:
                if re.search(rx, head, re.I):
                    tags.add(t)
                    break
            else:
                tags.add("trig_other")
    # whitelist pass: every clause must be expressible
    covered_text = True
    for l in lines:
        # keyword-only lines ("Flying, vigilance") are handled by tags
        if re.fullmatch(r"[A-Za-z ,\-—{}0-9]+", l) and not re.search(r"[:.]", l) and len(l) < 60:
            covered_text = False if l.strip() else covered_text
            continue
        for s in sentences(l):
            if not clause_ok(s):
                covered_text = False
    inert_exact = bool(lines) and all(INERT_MANA.match(s) for l in lines for s in sentences(l))
    if not lines:
        inert_exact = True  # vanilla: no text
    if inert_exact:
        tags.discard("mana_complex")
    return text, types, tags, covered_text, inert_exact


def load_forge(folder):
    index = {}
    for root, _, files in os.walk(folder):
        for fn in files:
            if not fn.endswith(".txt"):
                continue
            path = os.path.join(root, fn)
            with open(path, encoding="utf-8", errors="replace") as f:
                body = f.read()
            for m in re.finditer(r"^Name:(.+)$|FlavorName:(.+)$", body, re.M):
                if m.group(2):
                    index.setdefault(m.group(2).strip(), (path, body))
                    continue
                index.setdefault(m.group(1).strip(), (path, body))
            o = re.search(r"^Oracle:(.+)$", body, re.M)
            if o and len(o.group(1)) > 40:
                index.setdefault("oracle:" + o.group(1).strip().replace(BSN, NL), (path, body))
    return index


def forge_status(card, index):
    front = card["name"].split(" // ")[0]
    hit = index.get(card["name"]) or index.get(front)
    if not hit and card.get("oracle_text"):
        # Universes Beyond cards can sit under their other printed name
        own = card["oracle_text"].replace(card["name"], "CARDNAME")
        for key, val in index.items():
            if key.startswith("oracle:") and key[7:].replace(val[1].split(NL)[0][5:], "CARDNAME") == own:
                hit = val
                break
    if not hit:
        return "missing", None
    path, body = hit
    abilities = re.findall(r"^(A|T|S|R|K|SVar):", body, re.M)
    oracle = re.search(r"^Oracle:(.*)$", body, re.M)
    oracle_text = oracle.group(1).strip() if oracle else ""
    if not abilities and oracle_text and "Land" not in body.split("Types:")[1].split("\n")[0]:
        return "stub", path
    return "present", path


def main(forge_folder):
    sample = json.load(open(os.path.join(HERE, "sample.json"), encoding="utf-8"))
    cards = json.load(open(os.path.join(HERE, "cards.json"), encoding="utf-8"))
    live = [c for c in cards if not c["stale"]]
    by_oracle = {c["oracle_id"]: c for c in cards}
    by_name = {}
    for c in live + [c for c in cards if c["stale"]]:
        by_name.setdefault(c["name"], c)
        if " // " in c["name"]:
            by_name.setdefault(c["name"].split(" // ")[0], c)
    forge = load_forge(forge_folder)

    unresolved = Counter()
    slots = []  # (deck_source, deck_id, card)
    for d in sample["decks"]:
        for entry in d["cards"]:
            name, qty = entry[0], entry[1]
            c = by_oracle.get(entry[2]) if len(entry) > 2 else None
            c = c or by_name.get(name) or by_name.get(name.split(" // ")[0])
            if not c:
                unresolved[name] += qty
                continue
            for _ in range(qty):
                slots.append((d["source"], d["id"], c))

    info = {}
    for _, _, c in slots:
        if c["name"] in info:
            continue
        text, types, tags, covered_text, inert_exact = classify(c)
        fetch_ok = bool(c.get("fetch_activation")) and "Land" in types and len([l for l in text.split("\n") if l.strip()]) == 1
        engine = ("defined" if c["staple"] else
                  "inert_exact" if inert_exact and not tags else
                  "fetch_facet" if fetch_ok else
                  "expressible" if covered_text and not tags else
                  "uncovered")
        missing = sorted(tags) if engine == "uncovered" else []
        if engine == "uncovered" and not tags:
            missing = ["unclassified"]
        fstat, fpath = forge_status(c, forge)
        info[c["name"]] = {
            "types": types, "text": text, "tags": sorted(tags), "engine": engine,
            "missing": missing, "forge": fstat,
            "forge_todo": bool(fpath and re.search(r"^#\s*TODO", open(fpath, encoding="utf-8", errors="replace").read(), re.M)),
            "basic": c["name"] in BASICS, "rank": c.get("rank"),
        }

    json.dump({"unresolved": unresolved, "cards": info,
               "slots": [[s, i, c["name"]] for s, i, c in slots]},
              open(os.path.join(HERE, "results.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print("unknown keywords", UNKNOWN_KW.most_common(60))
    print("slots", len(slots), "unresolved", sum(unresolved.values()), dict(unresolved))


if __name__ == "__main__":
    main(sys.argv[1])
