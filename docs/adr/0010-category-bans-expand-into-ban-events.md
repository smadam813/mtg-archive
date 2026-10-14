# Category bans expand into ban events from a curated rules file

The official Commander list bans some cards by category line, not by name: ante cards, Conspiracy cards, and the seven cards Wizards removed in June 2020 for racist depictions. The banlist events file records only named cards, so the replay knew nothing of these 41 cards. Each ingest reported them as drift, and legality derived `legal` for them, because only a ban event produces `banned` (ADR 0004).

**A second curated file, `data/banlist-categories.json`, holds the category lines.** Each entry has a key, the list's wording, the date and announcement that put the ban in force, and one member rule from a closed vocabulary: `card_type`, `oracle_word` (a whole word, case-insensitive, in the Oracle text of the card or any face), or `cards` (exact Oracle names). The 2020 removals use `cards`, because no card field marks them.

**The replay phase expands each category into ordinary ban events.** Each member card gets a `ban` event with scope all at the category's date, and the event records the category key. A category on a date the events file already holds joins that version, so the two must cite the same announcement. Legality, citations, `GET /ban-events/{id}`, and drift detection then need no second path, and the plugin needs no new citation type.

**The dates follow the list, as the events file does.** Before 2020-05-18, ante and Conspiracy cards were banned only through the "Vintage-legal cards" baseline. The [18 May 2020 update](https://mtgcommander.net/index.php/2020/05/18/18-may-2020-update/) deleted that baseline, and the banned-list page printed both lines from then on. So both categories join the 2020-05-18 version with that URL. The RC never printed a line for the 2020 removals. Its [statement](https://mtgcommander.net/index.php/2020/06/14/rc-statement-on-wizards-removal-of-inappropriate-cards/) adopts the Wizards ban "Effective June 10th, 2020", so that category is dated 2020-06-10.

**The file stays curated truth, as ADR 0004 requires.** The expansion is a derivation from the corpus, not a snapshot diff: it never reads `legalities.commander`. A rule that matches no card, or a date collision, fails the run.

**Rejected alternatives.** Naming the 41 cards in the events file breaks the rule that the file records what the list named. It also breaks the Wayback property test, which compares against lists that print category lines. Deriving `not_legal` for members avoids a citation but is wrong for the 2020 removals: they were Commander-playable until 2020, and `not_legal` means never playable. Silencing the drift alone leaves `deck-check` calling Jihad legal.

**The same issue fixed the drift rule for companion bans.** Since 2026-02-09 Lutri is banned only as a companion. Scryfall says `legal` for it, so the detector now expects `banned` only for scope all.
