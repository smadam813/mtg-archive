using MtgArchive.Ingest;

namespace MtgArchive.Tests;

/// <summary>
/// The card feature derivation (spec §5.1) over real Oracle text: the closed
/// mana, draw and tutor template families, the land rule, reminder and
/// quoted text, and multi-face cards. A sentence that mentions draw, search
/// or add outside the families makes the face unprofiled; the expected line
/// then starts "unprofiled(sentence)". The response families (ADR 0013) read
/// counters and one-shot protection grants. A sentence that mentions an answer outside them ends the line
/// with "response_unprofiled(sentence)". Each row states the whole profile
/// as one <see cref="ProfileText"/> line.
/// </summary>
public sealed class CardFeatureDerivationTests
{
    [Theory]
    [InlineData("Sol Ring", "Artifact", "{T}: Add {C}{C}.", "C", "develops tap={C}{C} activated")]
    [InlineData("Birds of Paradise", "Creature — Bird", "Flying\n{T}: Add one mana of any color.", "B,G,R,U,W", "develops tap={W}|{U}|{B}|{R}|{G} activated")]
    [InlineData("Arcane Signet", "Artifact", "{T}: Add one mana of any color in your commander's color identity.", "B,G,R,U,W", "develops tap={W}|{U}|{B}|{R}|{G} activated")]
    [InlineData("Talisman of Dominance", "Artifact", "{T}: Add {C}.\n{T}: Add {U} or {B}. This artifact deals 1 damage to you.", "B,C,U", "develops tap={C}|{U}|{B} activated")]
    // A mana cost or a sacrifice: the ability develops, but the face is no free tap source.
    [InlineData("Dimir Signet", "Artifact", "{1}, {T}: Add {U}{B}.", "B,U", "develops activated")]
    [InlineData("Lotus Petal", "Artifact", "{T}, Sacrifice this artifact: Add one mana of any color.", "B,G,R,U,W", "develops activated")]
    [InlineData("Simian Spirit Guide", "Creature — Ape Spirit", "Exile this card from your hand: Add {R}.", "R", "develops hand={R} activated")]
    [InlineData("Dark Ritual", "Instant", "Add {B}{B}{B}.", "B", "develops spell={B}{B}{B}")]
    [InlineData("Seething Song", "Instant", "Add {R}{R}{R}{R}{R}.", "R", "develops spell={R}{R}{R}{R}{R}")]
    // "draw step" names a step, not a draw.
    [InlineData("Mana Vault", "Artifact", "This artifact doesn't untap during your untap step.\nAt the beginning of your upkeep, you may pay {4}. If you do, untap this artifact.\nAt the beginning of your draw step, if this artifact is tapped, it deals 1 damage to you.\n{T}: Add {C}{C}{C}.", "C", "develops tap={C}{C}{C} activated")]
    // Reminder text only: the basic land has no rules text, so no activated ability.
    [InlineData("Swamp", "Basic Land — Swamp", "({T}: Add {B}.)", "B", "develops tap={B}")]
    // A land taps for one option per produced color, as XMage plays it.
    [InlineData("Ancient Tomb", "Land", "{T}: Add {C}{C}. This land deals 2 damage to you.", "C", "develops tap={C} activated")]
    [InlineData("Llanowar Wastes", "Land", "{T}: Add {C}.\n{T}: Add {B} or {G}. This land deals 1 damage to you.", "B,C,G", "develops tap={B}|{C}|{G} activated")]
    // Quoted granted-ability text is not the card's own ability.
    [InlineData("Cryptolith Rite", "Enchantment", "Creatures you control have \"{T}: Add one mana of any color.\"", "B,G,R,U,W", "develops")]
    [InlineData("Pithing Needle", "Artifact", "As this artifact enters, choose a card name.\nActivated abilities of sources with the chosen name can't be activated unless they're mana abilities.", "", "")]
    // Outside the family: unprofiled, and the profile keeps what matched.
    [InlineData("Cabal Ritual", "Instant", "Add {B}{B}{B}.\nThreshold — Add {B}{B}{B}{B}{B} instead if there are seven or more cards in your graveyard.", "B", "unprofiled(Add {B}{B}{B}{B}{B} instead if there are seven or more cards in your graveyard) develops spell={B}{B}{B}")]
    [InlineData("Gilded Lotus", "Artifact", "{T}: Add three mana of any one color.", "B,G,R,U,W", "unprofiled(Add three mana of any one color) develops activated")]
    [InlineData("Fellwar Stone", "Artifact", "{T}: Add one mana of any color that a land an opponent controls could produce.", "B,G,R,U,W", "unprofiled(Add one mana of any color that a land an opponent controls could produce) develops activated")]
    // Mana from a trigger is outside the family.
    [InlineData("Priest of Gix", "Creature — Phyrexian Human Cleric Minion", "When this creature enters, add {B}{B}{B}.", "B", "unprofiled(When this creature enters, add {B}{B}{B}) develops")]
    // A delayed trigger inside a spell reads as a plain clause, not spell
    // mana: it is unprofiled rather than misread as a cast.
    // Mana paid from the graveyard is no battlefield mana: unprofiled. The
    // produced colors still make the face develop.
    [InlineData("Jack-o'-Lantern", "Artifact", "{1}, {T}, Sacrifice this artifact: Exile up to one target card from a graveyard. Draw a card.\n{1}, Exile this card from your graveyard: Add one mana of any color.", "B,G,R,U,W", "unprofiled(Add one mana of any color) develops draws activated")]
    // A cost that exiles another card from the graveyard is paid on the
    // battlefield.
    [InlineData("Titans' Nest", "Enchantment", "At the beginning of your upkeep, surveil 1. (Look at the top card of your library. You may put that card into your graveyard.)\nExile a card from your graveyard: Add {C}. Spend this mana only to cast a spell that's one or more colors without {X} in its mana cost.", "C", "develops activated")]
    // Scryfall lists the Treasure colors for any recipient. A token that only
    // another player gets gives the controller no mana.
    [InlineData("An Offer You Can't Refuse", "Instant", "Counter target noncreature spell. Its controller creates two Treasure tokens. (They're artifacts with \"{T}, Sacrifice this artifact: Add one mana of any color.\")", "B,G,R,U,W", "counter=spell/not=Creature")]
    [InlineData("Buy Your Silence", "Instant", "Exile target nonland permanent. Its controller creates a Treasure token.", "B,G,R,U,W", "")]
    [InlineData("Wanted Scoundrels", "Creature — Human Rogue", "When this creature dies, target opponent creates two Treasure tokens.", "B,G,R,U,W", "")]
    [InlineData("Bilbo's Gambit", "Instant", "Gift a Treasure (You may promise an opponent a gift as you cast this spell. If you do, they create a Treasure token before its other effects.)\nTarget creature you control gains hexproof until end of turn.", "B,G,R,U,W", "grants=hexproof:target_creature")]
    [InlineData("Blooming Blast", "Instant", "Gift a Treasure (You may promise an opponent a gift as you cast this spell. If you do, they create a Treasure token before its other effects.)\nBlooming Blast deals 2 damage to target creature. If the gift was promised, Blooming Blast also deals 3 damage to that creature's controller.", "B,G,R,U,W", "")]
    [InlineData("Vraska, the Cutting Glare", "Legendary Planeswalker — Vraska", "+1: Destroy target permanent an opponent controls. They create a Treasure token.", "B,G,R,U,W", "activated")]
    // A token for the controller keeps the colors.
    [InlineData("Smothering Tithe", "Enchantment", "Whenever an opponent draws a card, that player may pay {2}. If the player doesn't, you create a Treasure token.", "B,G,R,U,W", "develops")]
    [InlineData("Marching Duodrone", "Artifact Creature — Construct", "When this creature enters, each player creates a Treasure token.", "B,G,R,U,W", "develops")]
    // One token for the controller keeps the colors.
    [InlineData("Test Both Tokens", "Instant", "Create a Treasure token. Target opponent creates a Treasure token.", "B,G,R,U,W", "develops")]
    [InlineData("Test One Sentence", "Instant", "Target opponent creates a Treasure token and you create a Treasure token.", "B,G,R,U,W", "develops")]
    [InlineData("Test Gift And Own", "Instant", "Gift a Treasure (You may promise an opponent a gift as you cast this spell. If you do, they create a Treasure token before its other effects.)\nCreate a Treasure token.", "B,G,R,U,W", "develops")]
    // Mana that no template matches still shows in the colors.
    [InlineData("Test Gift And Odd Mana", "Artifact", "Gift a Treasure (You may promise an opponent a gift as you cast this spell. If you do, they create a Treasure token before its other effects.)\n{T}: Add three mana of any one color.", "B,G,R,U,W", "unprofiled(Add three mana of any one color) develops activated")]
    // "Mana value" is no mana.
    [InlineData("Test Mana Value", "Instant", "Add a +1/+1 counter to target creature with mana value 3 or less. Target opponent creates a Treasure token.", "B,G,R,U,W", "unprofiled(Add a +1/+1 counter to target creature with mana value 3 or less)")]
    // "They" with no opponent before it is an unknown recipient.
    [InlineData("Test Bare They", "Instant", "Destroy target permanent. They create a Treasure token.", "B,G,R,U,W", "develops")]
    // Gold and Powerstone tokens follow the Treasure rule.
    [InlineData("Test Gold Gift", "Instant", "Gift a Gold (You may promise an opponent a gift as you cast this spell. If you do, they create a Gold token before its other effects.)\nDraw a card.", "B,G,R,U,W", "draws")]
    [InlineData("Test Powerstone", "Sorcery", "Target opponent creates a Powerstone token.", "C", "")]
    [InlineData("Reckless Blaze", "Sorcery", "Reckless Blaze deals 5 damage to each creature. Whenever a creature you control dealt damage this way dies this turn, add {R}.", "", "unprofiled(Whenever a creature you control dealt damage this way dies this turn, add {R})")]
    public void Mana_family(string name, string typeLine, string oracleText, string produced, string expected)
        => Assert.Equal(expected, Describe(name, typeLine, oracleText, produced));

    [Theory]
    [InlineData("Opt", "Instant", "Scry 1. (Look at the top card of your library. You may put that card on the bottom.)\nDraw a card.", "", "draws")]
    [InlineData("Preordain", "Sorcery", "Scry 2, then draw a card. (To scry 2, look at the top two cards of your library, then put any number of them on the bottom and the rest on top in any order.)", "", "draws")]
    [InlineData("Brainstorm", "Instant", "Draw three cards, then put two cards from your hand on top of your library in any order.", "", "draws")]
    [InlineData("Night's Whisper", "Sorcery", "You draw two cards and lose 2 life.", "", "draws")]
    [InlineData("Phyrexian Arena", "Enchantment", "At the beginning of your upkeep, you draw a card and lose 1 life.", "", "draws")]
    [InlineData("Mulldrifter", "Creature — Elemental", "Flying\nWhen this creature enters, draw two cards.\nEvoke {2}{U} (You may cast this spell for its evoke cost. If you do, it's sacrificed when it enters.)", "", "draws")]
    [InlineData("Skullclamp", "Artifact — Equipment", "Equipped creature gets +1/-1.\nWhenever equipped creature dies, draw two cards.\nEquip {1}", "", "draws activated")]
    [InlineData("Mind Stone", "Artifact", "{T}: Add {C}.\n{1}, {T}, Sacrifice this artifact: Draw a card.", "C", "develops draws tap={C} activated")]
    [InlineData("Chromatic Star", "Artifact", "{1}, {T}, Sacrifice this artifact: Add one mana of any color.\nWhen this artifact is put into a graveyard from the battlefield, draw a card.", "B,G,R,U,W", "develops draws activated")]
    [InlineData("Arch of Orazca", "Land", "Ascend (If you control ten or more permanents, you get the city's blessing for the rest of the game.)\n{T}: Add {C}.\n{5}, {T}: Draw a card. Activate only if you have the city's blessing.", "C", "develops draws tap={C} activated")]
    // An opponent's draw matches and adds nothing.
    [InlineData("Test Opponent Draw", "Sorcery", "Target opponent draws two cards.", "", "")]
    // "Each player" includes the controller. XMage picks the target of a
    // "target player" draw, and its AI targets itself for a draw
    // (Outcome.DrawCard). Both are the controller's draw.
    [InlineData("Sign in Blood", "Sorcery", "Target player draws two cards and loses 2 life.", "", "draws")]
    [InlineData("Temple Bell", "Artifact", "{T}: Each player draws a card.", "", "draws activated")]
    [InlineData("Vision Skeins", "Instant", "Each player draws two cards.", "", "draws")]
    [InlineData("Faerie Mastermind", "Creature — Faerie Rogue", "Flash\nFlying\nWhenever an opponent draws their second card each turn, you draw a card.\n{3}{U}: Each player draws a card.", "", "draws activated")]
    // A bare verb after ", then" takes the subject of the clause before it.
    [InlineData("Windfall", "Sorcery", "Each player discards their hand, then draws cards equal to the greatest number of cards a player discarded this way.", "", "draws")]
    [InlineData("Wheel of Fortune", "Sorcery", "Each player discards their hand, then draws seven cards.", "", "draws")]
    [InlineData("Test Opponent Wheel", "Sorcery", "Each opponent discards a card, then draws a card.", "", "")]
    // A bare "draws" after a subject the family does not know stays
    // unprofiled.
    [InlineData("Oblation", "Instant", "The owner of target nonland permanent shuffles it into their library, then draws two cards.", "", "unprofiled(The owner of target nonland permanent shuffles it into their library, then draws two cards)")]
    [InlineData("Test Qualified Wheel", "Sorcery", "Each player who has no cards in hand discards their hand, then draws seven cards.", "", "unprofiled(Each player who has no cards in hand discards their hand, then draws seven cards)")]
    // A Treasure is no template mana, so the draw counts and the face does
    // not develop.
    [InlineData("Deadly Dispute", "Instant", "As an additional cost to cast this spell, sacrifice an artifact or creature.\nDraw two cards and create a Treasure token.", "", "draws")]
    // "That player" can be anyone: unprofiled, never a silent guess.
    [InlineData("Howling Mine", "Artifact", "At the beginning of each player's draw step, if this artifact is untapped, that player draws an additional card.", "", "unprofiled(At the beginning of each player's draw step, if this artifact is untapped, that player draws an additional card)")]
    // A draw in a trigger condition is no draw.
    [InlineData("Sheoldred, the Apocalypse", "Legendary Creature — Phyrexian Praetor", "Deathtouch\nWhenever you draw a card, you gain 2 life.\nWhenever an opponent draws a card, they lose 2 life.", "", "")]
    // The face names itself, and its name holds "Search".
    [InlineData("Search for Azcanta", "Legendary Enchantment", "At the beginning of your upkeep, surveil 1. Then if you have seven or more cards in your graveyard, you may transform Search for Azcanta. (Look at the top card of your library. You may put that card into your graveyard.)", "", "")]
    // Mentions draw, matches no template.
    [InlineData("Valakut Awakening", "Instant", "Put any number of cards from your hand on the bottom of your library, then draw that many cards plus one.", "", "unprofiled(Put any number of cards from your hand on the bottom of your library, then draw that many cards plus one)")]
    [InlineData("Rhystic Study", "Enchantment", "Whenever an opponent casts a spell, you may draw a card unless that player pays {1}.", "", "unprofiled(Whenever an opponent casts a spell, you may draw a card unless that player pays {1})")]
    // The Treasure is for the controller, so the Scryfall colors keep it.
    [InlineData("Deadly Dispute", "Instant", "As an additional cost to cast this spell, sacrifice an artifact or creature.\nDraw two cards and create a Treasure token.", "B,G,R,U,W", "develops draws")]
    // A draw with a trailing life loss or a leading life gain still counts.
    [InlineData("Dusk Legion Zealot", "Creature — Vampire Soldier", "When this creature enters, you draw a card and you lose 1 life.", "", "draws")]
    [InlineData("Test Gain", "Sorcery", "You gain 2 life and draw a card.", "", "draws")]
    [InlineData("Test Gain Trailing", "Sorcery", "You draw a card and you gain 1 life.", "", "draws")]
    // A draw paid from the graveyard is still the controller's draw.
    [InlineData("Faerie Dreamthief", "Creature — Faerie Warlock", "Flying\nWhen this creature enters, surveil 1. (Look at the top card of your library. You may put it into your graveyard.)\n{2}{B}, Exile this card from your graveyard: You draw a card and you lose 1 life.", "", "draws activated")]
    // Cycling reads as the activated ability its reminder text states.
    [InlineData("Street Wraith", "Creature — Wraith", "Swampwalk\nCycling—Pay 2 life. (Pay 2 life, Discard this card: Draw a card.)", "", "draws activated")]
    // A cycling line with no reminder text has no effect to read.
    [InlineData("Test Bare Cycling", "Creature — Beast", "Cycling {2}", "", "unprofiled(Cycling {2}) activated")]
    public void Draw_family(string name, string typeLine, string oracleText, string produced, string expected)
        => Assert.Equal(expected, Describe(name, typeLine, oracleText, produced));

    [Theory]
    [InlineData("Demonic Tutor", "Sorcery", "Search your library for a card, put that card into your hand, then shuffle.", "", "tutor=hand/cast")]
    [InlineData("Vampiric Tutor", "Instant", "Search your library for a card, then shuffle and put that card on top. You lose 2 life.", "", "tutor=library_top/cast")]
    [InlineData("Mystical Tutor", "Instant", "Search your library for an instant or sorcery card, reveal it, then shuffle and put that card on top.", "", "tutor=library_top/cast")]
    [InlineData("Worldly Tutor", "Instant", "Search your library for a creature card, reveal it, then shuffle and put the card on top.", "", "tutor=library_top/cast")]
    [InlineData("Entomb", "Instant", "Search your library for a card, put that card into your graveyard, then shuffle.", "", "tutor=graveyard/cast")]
    [InlineData("Rampant Growth", "Sorcery", "Search your library for a basic land card, put that card onto the battlefield tapped, then shuffle.", "", "develops tutor=battlefield+tapped/cast")]
    [InlineData("Farseek", "Sorcery", "Search your library for a Plains, Island, Swamp, or Mountain card, put it onto the battlefield tapped, then shuffle.", "", "develops tutor=battlefield+tapped/cast")]
    [InlineData("Harrow", "Instant", "As an additional cost to cast this spell, sacrifice a land.\nSearch your library for up to two basic land cards, put them onto the battlefield, then shuffle.", "", "develops tutor=battlefield/cast")]
    // The fetch lands: Evolving Wilds and a Zendikar fetch.
    [InlineData("Evolving Wilds", "Land", "{T}, Sacrifice this land: Search your library for a basic land card, put it onto the battlefield tapped, then shuffle.", "", "develops tutor=battlefield+tapped/activate_on_battlefield activated")]
    [InlineData("Arid Mesa", "Land", "{T}, Pay 1 life, Sacrifice this land: Search your library for a Mountain or Plains card, put it onto the battlefield, then shuffle.", "", "develops tutor=battlefield/activate_on_battlefield activated")]
    [InlineData("Fabled Passage", "Land", "{T}, Sacrifice this land: Search your library for a basic land card, put it onto the battlefield tapped, then shuffle. Then if you control four or more lands, untap that land.", "", "develops tutor=battlefield+tapped/activate_on_battlefield activated")]
    [InlineData("Wayfarer's Bauble", "Artifact", "{2}, {T}, Sacrifice this artifact: Search your library for a basic land card, put that card onto the battlefield tapped, then shuffle.", "", "develops tutor=battlefield+tapped/activate_on_battlefield/{2} activated")]
    [InlineData("Expedition Map", "Artifact", "{2}, {T}, Sacrifice this artifact: Search your library for a land card, reveal it, put it into your hand, then shuffle.", "", "tutor=hand/activate_on_battlefield/{2} activated")]
    [InlineData("Sakura-Tribe Elder", "Creature — Snake Shaman", "Sacrifice this creature: Search your library for a basic land card, put that card onto the battlefield tapped, then shuffle.", "", "develops tutor=battlefield+tapped/activate_on_battlefield activated")]
    [InlineData("Burnished Hart", "Artifact Creature — Elk", "{3}, Sacrifice this creature: Search your library for up to two basic land cards, put them onto the battlefield tapped, then shuffle.", "", "develops tutor=battlefield+tapped/activate_on_battlefield/{3} activated")]
    // A search by "its controller" or "that player" can be the
    // controller's: unprofiled.
    [InlineData("Path to Exile", "Instant", "Exile target creature. Its controller may search their library for a basic land card, put that card onto the battlefield tapped, then shuffle.", "", "unprofiled(Its controller may search their library for a basic land card, put that card onto the battlefield tapped, then shuffle)")]
    [InlineData("Boseiju, Who Endures", "Legendary Land", "{T}: Add {G}.\nChannel — {1}{G}, Discard this card: Destroy target artifact, enchantment, or nonbasic land an opponent controls. That player may search their library for a land card with a basic land type, put it onto the battlefield, then shuffle. This ability costs {1} less to activate for each legendary creature you control.", "G", "unprofiled(That player may search their library for a land card with a basic land type, put it onto the battlefield, then shuffle) develops tap={G} activated")]
    // Typecycling and transmute read as the hand activation their reminder
    // text states.
    [InlineData("Ash Barrens", "Land", "{T}: Add {C}.\nBasic landcycling {1} ({1}, Discard this card: Search your library for a basic land card, reveal it, put it into your hand, then shuffle.)", "C", "develops tap={C} tutor=hand/activate_from_hand/{1} activated")]
    [InlineData("Tolaria West", "Land", "This land enters tapped.\n{T}: Add {U}.\nTransmute {1}{U}{U} ({1}{U}{U}, Discard this card: Search your library for a card with mana value 0, reveal it, put it into your hand, then shuffle. Transmute only as a sorcery.)", "U", "develops tap={U} tutor=hand/activate_from_hand/{1}{U}{U} activated")]
    // A Saga chapter's search resolves chapter − 1 turns after the Saga
    // enters (CR 714.2b, 714.3b). Quoted granted abilities are not the face's.
    [InlineData("Urza's Saga", "Enchantment Land — Urza's Saga", "(As this Saga enters and after your draw step, add a lore counter. Sacrifice after III.)\nI — This Saga gains \"{T}: Add {C}.\"\nII — This Saga gains \"{2}, {T}: Create a 0/0 colorless Construct artifact creature token with 'This token gets +1/+1 for each artifact you control.'\"\nIII — Search your library for an artifact card with mana cost {0} or {1}, put it onto the battlefield, then shuffle.", "C", "develops tap={C} tutor=battlefield/saga_chapter/delay=2")]
    [InlineData("The Restoration of Eiganjo", "Enchantment — Saga", "(As this Saga enters and after your draw step, add a lore counter.)\nI — Search your library for a basic Plains card, reveal it, put it into your hand, then shuffle.\nII — You may discard a card. When you do, return target permanent card with mana value 2 or less from your graveyard to the battlefield tapped.\nIII — Exile this Saga, then return it to the battlefield transformed under your control.", "", "tutor=hand/saga_chapter")]
    // A combined chapter line resolves first at its first chapter.
    [InlineData("Test Combined Saga", "Enchantment — Saga", "II, III — Search your library for a card, put that card into your hand, then shuffle.", "", "tutor=hand/saga_chapter/delay=1")]
    // A random discard after the put is part of the template.
    [InlineData("Gamble", "Sorcery", "Search your library for a card, put that card into your hand, discard a card at random, then shuffle.", "", "tutor=hand/cast")]
    // Mentions search, matches no template.
    [InlineData("Kodama's Reach", "Sorcery — Arcane", "Search your library for up to two basic land cards, reveal those cards, put one onto the battlefield tapped and the other into your hand, then shuffle.", "", "unprofiled(Search your library for up to two basic land cards, reveal those cards, put one onto the battlefield tapped and the other into your hand, then shuffle)")]
    // A search in a triggered ability is outside the family.
    [InlineData("Wood Elves", "Creature — Elf Scout", "When this creature enters, search your library for a Forest card, put that card onto the battlefield, then shuffle.", "", "unprofiled(When this creature enters, search your library for a Forest card, put that card onto the battlefield, then shuffle)")]
    [InlineData("Ranger-Captain of Eos", "Creature — Human Soldier Ranger", "When this creature enters, you may search your library for a creature card with mana value 1 or less, reveal it, put it into your hand, then shuffle.\nSacrifice this creature: Your opponents can't cast noncreature spells this turn.", "", "unprofiled(When this creature enters, you may search your library for a creature card with mana value 1 or less, reveal it, put it into your hand, then shuffle) activated")]
    // A delayed trigger inside an instant is a plain clause, not a search:
    // unprofiled rather than misread as a cast tutor.
    [InlineData("Rushed Rebirth", "Instant", "Choose target creature. When that creature dies this turn, search your library for a creature card with lesser mana value, put it onto the battlefield tapped, then shuffle.", "", "unprofiled(When that creature dies this turn, search your library for a creature card with lesser mana value, put it onto the battlefield tapped, then shuffle)")]
    // A tutor paid from the graveyard is outside the family: no TutorVia
    // for it, so it is unprofiled and sets neither Develops nor Tutor.
    [InlineData("Colossal Rattlewurm", "Creature — Plant Serpent", "{1}{G}, Exile this card from your graveyard: Search your library for a Forest card, put it onto the battlefield tapped, then shuffle.", "", "unprofiled(Search your library for a Forest card, put it onto the battlefield tapped, then shuffle) activated")]
    public void Tutor_family(string name, string typeLine, string oracleText, string produced, string expected)
        => Assert.Equal(expected, Describe(name, typeLine, oracleText, produced));

    [Theory]
    // The done-query 2 final 99. An Offer You Can't Refuse is a Mana_family row.
    [InlineData("Arcane Denial", "Instant", "Counter target spell. Its controller may draw up to two cards at the beginning of the next turn's upkeep.\nYou draw a card at the beginning of the next turn's upkeep.", "", "unprofiled(Its controller may draw up to two cards at the beginning of the next turn's upkeep) counter=spell")]
    [InlineData("Flusterstorm", "Instant", "Counter target instant or sorcery spell unless its controller pays {1}.\nStorm (When you cast this spell, copy it for each spell cast before it this turn. You may choose new targets for the copies.)", "", "counter=spell/types=Instant|Sorcery/unless={1}")]
    [InlineData("Mana Drain", "Instant", "Counter target spell. At the beginning of your next main phase, add an amount of {C} equal to that spell's mana value.", "", "unprofiled(At the beginning of your next main phase, add an amount of {C} equal to that spell's mana value) counter=spell")]
    [InlineData("Pact of Negation", "Instant", "Counter target spell.\nAt the beginning of your next upkeep, pay {3}{U}{U}. If you don't, you lose the game.", "", "counter=spell/loses_game")]
    // brawl-seek-conjure. The bq1 and bq2 decks hold no counterspell. A
    // "+1/+1 counter" (Ajani's Pridemate, bq2) is no answer.
    [InlineData("Kindred Denial", "Instant", "Counter target spell. Seek a card with the same mana value as that spell.", "", "counter=spell")]
    [InlineData("Ajani's Pridemate", "Creature — Cat Soldier", "Whenever you gain life, put a +1/+1 counter on this creature.", "", "")]
    // Any spell, with a side effect or an alternative cost.
    [InlineData("Counterspell", "Instant", "Counter target spell.", "", "counter=spell")]
    [InlineData("Force of Will", "Instant", "You may pay 1 life and exile a blue card from your hand rather than pay this spell's mana cost.\nCounter target spell.", "", "counter=spell")]
    [InlineData("Dissipate", "Instant", "Counter target spell. If that spell is countered this way, exile it instead of putting it into its owner's graveyard.", "", "counter=spell")]
    [InlineData("Remand", "Instant", "Counter target spell. If that spell is countered this way, put it into its owner's hand instead of into that player's graveyard.\nDraw a card.", "", "draws counter=spell")]
    // The scripted opponent controls the disruption card.
    [InlineData("Counterflux", "Instant", "This spell can't be countered.\nCounter target spell you don't control.\nOverload {1}{U}{U}{R} (You may cast this spell for its overload cost. If you do, change \"target\" in its text to \"each.\")", "", "counter=spell")]
    [InlineData("Disallow", "Instant", "Counter target spell, activated ability, or triggered ability. (Mana abilities can't be targeted.)", "", "counter=spell")]
    // An excluded type and a type list.
    [InlineData("Negate", "Instant", "Counter target noncreature spell.", "", "counter=spell/not=Creature")]
    [InlineData("Dovin's Veto", "Instant", "This spell can't be countered.\nCounter target noncreature spell.", "", "counter=spell/not=Creature")]
    [InlineData("Annul", "Instant", "Counter target artifact or enchantment spell.", "", "counter=spell/types=Artifact|Enchantment")]
    [InlineData("Swan Song", "Instant", "Counter target enchantment, instant, or sorcery spell. Its controller creates a 2/2 blue Bird creature token with flying.", "", "counter=spell/types=Enchantment|Instant|Sorcery")]
    [InlineData("Dispel", "Instant", "Counter target instant spell.", "", "counter=spell/types=Instant")]
    [InlineData("Envelop", "Instant", "Counter target sorcery spell.", "", "counter=spell/types=Sorcery")]
    // Creature spells only: stored, and no disruption card is a creature.
    [InlineData("Essence Scatter", "Instant", "Counter target creature spell.", "", "counter=spell/types=Creature")]
    // A color, in either wording, in the first mode.
    [InlineData("Red Elemental Blast", "Instant", "Choose one —\n• Counter target blue spell.\n• Destroy target blue permanent.", "", "counter=spell/colors=U")]
    [InlineData("Pyroblast", "Instant", "Choose one —\n• Counter target spell if it's blue.\n• Destroy target permanent if it's blue.", "", "counter=spell/colors=U")]
    [InlineData("Hydroblast", "Instant", "Choose one —\n• Counter target spell if it's red.\n• Destroy target permanent if it's red.", "", "counter=spell/colors=R")]
    // An ability: stored, and no disruption card is an ability.
    [InlineData("Stifle", "Instant", "Counter target activated or triggered ability. (Mana abilities can't be targeted.)", "", "counter=ability")]
    // "Unless its controller pays".
    [InlineData("Mana Leak", "Instant", "Counter target spell unless its controller pays {3}.", "", "counter=spell/unless={3}")]
    [InlineData("Daze", "Instant", "You may return an Island you control to its owner's hand rather than pay this spell's mana cost.\nCounter target spell unless its controller pays {1}.", "", "counter=spell/unless={1}")]
    [InlineData("Syncopate", "Instant", "Counter target spell unless its controller pays {X}. If that spell is countered this way, exile it instead of putting it into its owner's graveyard.", "", "counter=spell/unless={X}")]
    [InlineData("Spell Pierce", "Instant", "Counter target noncreature spell unless its controller pays {2}.", "", "counter=spell/not=Creature/unless={2}")]
    // The first mode only. A later mode's draw still counts for the draw
    // family.
    [InlineData("Izzet Charm", "Instant", "Choose one —\n• Counter target noncreature spell unless its controller pays {2}.\n• Izzet Charm deals 2 damage to target creature.\n• Draw two cards, then discard two cards.", "", "draws counter=spell/not=Creature/unless={2}")]
    [InlineData("Mystic Confluence", "Instant", "Choose three. You may choose the same mode more than once.\n• Counter target spell unless its controller pays {3}.\n• Return target creature to its owner's hand.\n• Draw a card.", "", "draws counter=spell/unless={3}")]
    [InlineData("Test Later Mode", "Instant", "Choose one —\n• Destroy target artifact.\n• Counter target spell.", "", "response_unprofiled(Counter target spell)")]
    // Outside the families: a trigger, an activation, a qualifier.
    [InlineData("Mystic Snake", "Creature — Snake", "Flash\nWhen this creature enters, counter target spell.", "", "response_unprofiled(When this creature enters, counter target spell)")]
    [InlineData("Test Activated Counter", "Creature — Human Wizard", "{U}, Sacrifice this creature: Counter target spell.", "", "activated response_unprofiled(Counter target spell)")]
    [InlineData("Tale's End", "Instant", "Counter target activated ability, triggered ability, or legendary spell.", "", "response_unprofiled(Counter target activated ability, triggered ability, or legendary spell)")]
    [InlineData("Test Mana Value Counter", "Instant", "Counter target spell with mana value 2 or less.", "", "response_unprofiled(Counter target spell with mana value 2 or less)")]
    [InlineData("Test Paid Per Card", "Instant", "Counter target spell unless its controller pays {1} for each card in your hand.", "", "response_unprofiled(Counter target spell unless its controller pays {1} for each card in your hand)")]
    // "Can't be countered" and a pact with no counter are no counter.
    [InlineData("Abrupt Decay", "Instant", "This spell can't be countered.\nDestroy target nonland permanent with mana value 3 or less.", "", "")]
    [InlineData("Slaughter Pact", "Instant", "Destroy target nonblack creature.\nAt the beginning of your next upkeep, pay {2}{B}. If you don't, you lose the game.", "", "")]
    public void Response_family(string name, string typeLine, string oracleText, string produced, string expected)
        => Assert.Equal(expected, Describe(name, typeLine, oracleText, produced));

    /// <summary>The protection families over real Oracle text.
    /// Each keyword of a grant is one grant "grant:scope", in text order.</summary>
    [Theory]
    // A team grant until end of turn.
    [InlineData("Heroic Intervention", "Instant", "Permanents you control gain hexproof and indestructible until end of turn.", "grants=hexproof:permanents|indestructible:permanents")]
    [InlineData("Flawless Maneuver", "Instant", "If you control a commander, you may cast this spell without paying its mana cost.\nCreatures you control gain indestructible until end of turn.", "grants=indestructible:creatures")]
    // Phase-out. "You gain protection from everything" protects no spell and no permanent: no mention.
    [InlineData("Teferi's Protection", "Instant", "Until your next turn, your life total can't change and you gain protection from everything. All permanents you control phase out. (While they're phased out, they're treated as though they don't exist. They phase in before you untap during your untap step.)\nExile Teferi's Protection.", "grants=phase_out:permanents")]
    [InlineData("Slip Out the Back", "Instant", "Put a +1/+1 counter on target creature. It phases out. (Treat it and anything attached to it as though they don't exist until its controller's next turn.)", "grants=phase_out:target_creature")]
    // A target grant, and the follow-up sentence that refers back to the target.
    [InlineData("Snakeskin Veil", "Instant", "Put a +1/+1 counter on target creature you control. It gains hexproof until end of turn. (It can't be the target of spells or abilities your opponents control.)", "grants=hexproof:target_creature")]
    [InlineData("Ranger's Guile", "Instant", "Target creature you control gets +1/+1 and gains hexproof until end of turn. (It can't be the target of spells or abilities your opponents control.)", "grants=hexproof:target_creature")]
    [InlineData("Tamiyo's Safekeeping", "Instant", "Target permanent you control gains hexproof and indestructible until end of turn. You gain 2 life. (A permanent with hexproof and indestructible can't be the target of spells or abilities your opponents control. Damage and effects that say \"destroy\" don't destroy it.)", "grants=hexproof:target_permanent|indestructible:target_permanent")]
    [InlineData("Test Protection From Red", "Instant", "Target creature you control gains protection from red until end of turn.", "grants=protection_from_red:target_creature")]
    // "Can't be countered" this turn, and hexproof from two colors.
    [InlineData("Veil of Summer", "Instant", "Draw a card if an opponent has cast a blue or black spell this turn. Spells you control can't be countered this turn. You and permanents you control gain hexproof from blue and from black until end of turn. (You and they can't be the targets of blue or black spells or abilities your opponents control.)", "unprofiled(Draw a card if an opponent has cast a blue or black spell this turn) grants=cant_be_countered:spells|hexproof_from_blue:permanents|hexproof_from_black:permanents")]
    // A grant under a condition is outside the families; the unconditional grant stays.
    [InlineData("Dawn's Truce", "Instant", "Gift a card (You may promise an opponent a gift as you cast this spell. If you do, they draw a card before its other effects.)\nYou and permanents you control gain hexproof until end of turn. If the gift was promised, permanents you control also gain indestructible until end of turn.", "grants=hexproof:permanents response_unprofiled(If the gift was promised, permanents you control also gain indestructible until end of turn)")]
    // Outside the families: a later mode, a color XMage chooses, an activated self-grant, any number of targets.
    [InlineData("Boros Charm", "Instant", "Choose one —\n• Boros Charm deals 4 damage to target player or planeswalker.\n• Permanents you control gain indestructible until end of turn.\n• Target creature gains double strike until end of turn.", "response_unprofiled(Permanents you control gain indestructible until end of turn)")]
    [InlineData("Gods Willing", "Instant", "Target creature you control gains protection from the color of your choice until end of turn. (It can't be blocked, targeted, dealt damage, enchanted, or equipped by anything of that color.)\nScry 1.", "response_unprofiled(Target creature you control gains protection from the color of your choice until end of turn)")]
    [InlineData("Test Activated Grant", "Creature — Elf", "{G}: This creature gains hexproof until end of turn.", "activated response_unprofiled(This creature gains hexproof until end of turn)")]
    [InlineData("Clever Concealment", "Instant", "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nAny number of target nonland permanents you control phase out. (Treat them and anything attached to them as though they don't exist until your next turn.)", "response_unprofiled(Any number of target nonland permanents you control phase out)")]
    // Static and attached grants get no family and are not unprofiled: XMage enforces them.
    [InlineData("Swiftfoot Boots", "Artifact — Equipment", "Equipped creature has hexproof and haste. (It can't be the target of spells or abilities your opponents control. It can attack and {T} no matter when it came under your control.)\nEquip {1} ({1}: Attach to target creature you control. Equip only as a sorcery.)", "activated")]
    [InlineData("Hexing Squelcher", "Creature — Goblin Sorcerer", "This spell can't be countered.\nWard—Pay 2 life.\nSpells you control can't be countered.\nOther creatures you control have \"Ward—Pay 2 life.\"", "")]
    // A mana rider and a grant to the player alone are no answer.
    [InlineData("Delighted Halfling", "Creature — Halfling Citizen", "{T}: Add {C}.\n{T}: Add one mana of any color. Spend this mana only to cast a legendary spell, and that spell can't be countered.", "develops tap={C}|{W}|{U}|{B}|{R}|{G} activated")]
    [InlineData("The One Ring", "Legendary Artifact", "Indestructible\nWhen The One Ring enters, if you cast it, you gain protection from everything until your next turn.\nAt the beginning of your upkeep, you lose 1 life for each burden counter on The One Ring.\n{T}: Put a burden counter on The One Ring, then draw a card for each burden counter on The One Ring.", "unprofiled(Put a burden counter on The One Ring, then draw a card for each burden counter on The One Ring) activated")]
    public void Protection_family(string name, string typeLine, string oracleText, string expected)
        => Assert.Equal(expected, Describe(name, typeLine, oracleText, name == "Delighted Halfling" ? "B,C,G,R,U,W" : ""));

    [Theory]
    // The disruption cards: Counterspell (blue instant), Atomize (black and
    // green instant), Rout (white sorcery).
    [InlineData("Counter target spell.", "Instant", "U", true)]
    [InlineData("Counter target noncreature spell.", "Sorcery", "W", true)]
    [InlineData("Counter target creature spell.", "Instant", "U", false)]
    [InlineData("Counter target instant or sorcery spell unless its controller pays {1}.", "Sorcery", "W", true)]
    [InlineData("Counter target sorcery spell.", "Instant", "B,G", false)]
    [InlineData("Choose one —\n• Counter target blue spell.\n• Destroy target blue permanent.", "Instant", "U", true)]
    [InlineData("Choose one —\n• Counter target blue spell.\n• Destroy target blue permanent.", "Instant", "B,G", false)]
    [InlineData("Choose one —\n• Counter target spell if it's red.\n• Destroy target permanent if it's red.", "Instant", "U", false)]
    [InlineData("Counter target activated or triggered ability.", "Instant", "U", false)]
    public void A_counter_reaches_a_spell_by_its_types_and_colors(string oracleText, string spellType, string spellColors, bool reaches)
    {
        var face = new CardFeatureDerivation.Face("Test Counter", ["Instant"], oracleText, null);
        var counter = CardFeatureDerivation.Derive(face).Profile.Response!.Counter!;
        Assert.Equal(reaches, counter.Reaches([spellType], spellColors.Split(',')));
    }

    [Theory]
    // A keyword activated ability (CR 702) sets HasActivatedAbility from
    // its keyword line alone, its cost format included.
    [InlineData("Lightning Greaves", "Artifact — Equipment", "Equipped creature has haste and shroud.\nEquip {0}", "", "activated")]
    // "Cycling abilities you activate cost..." names the keyword rather
    // than paying it: no cost follows directly, so it does not match.
    [InlineData("Test Card", "Enchantment", "Cycling abilities you activate cost up to {2} less to activate.", "", "")]
    [InlineData("Test Saddle", "Creature — Dinosaur", "Saddle 1 (Tap any number of other creatures you control with total power 1 or more: This creature becomes saddled until end of turn.)", "", "activated")]
    // A qualified equip (CR 702.6e): the keyword, then words, then its cost.
    [InlineData("Test Equip Type", "Artifact — Equipment", "Equipped creature gets +1/+1.\nEquip Dinosaur {1}", "", "activated")]
    [InlineData("Test Equip Words", "Artifact — Equipment", "Equipped creature gets +1/+1.\nEquip legendary creature {3}", "", "activated")]
    // "Equip abilities you activate cost..." names the keyword rather than
    // paying it: "abilities" is excluded from the qualifier, so no cost
    // follows directly and it does not match.
    [InlineData("Auriok Steelshaper", "Artifact Creature — Human Soldier", "Equip abilities you activate cost {1} less to activate.", "", "")]
    public void Keyword_activated_ability(string name, string typeLine, string oracleText, string produced, string expected)
        => Assert.Equal(expected, Describe(name, typeLine, oracleText, produced));

    // Its triggered draw is "you may draw a card": may is outside the plain
    // draw clause, so it is unprofiled. Its Crew line sets HasActivatedAbility.
    [Fact]
    public void A_vehicles_optional_draw_is_unprofiled_and_its_crew_sets_activated()
        => Assert.Equal(
            "unprofiled(Whenever this Vehicle attacks or blocks, you may draw a card) activated",
            Describe(
                "Smuggler's Copter",
                "Artifact — Vehicle",
                "Flying\nWhenever this Vehicle attacks or blocks, you may draw a card. If you do, discard a card.\nCrew 1 (Tap any number of untapped creatures you control with total power 1 or more: This Vehicle becomes an artifact creature until end of turn.)",
                ""));

    [Fact]
    public void A_pathway_face_taps_only_for_its_own_color()
        => Assert.Equal(
            ["develops tap={W} activated", "develops tap={B} activated"],
            DescribeFaces("Brightclimb Pathway // Grimclimb Pathway", "Land // Land", ["B", "W"],
                new("Brightclimb Pathway", "Land", "{T}: Add {W}.", ["W"]),
                new("Grimclimb Pathway", "Land", "{T}: Add {B}.", ["B"])));

    [Fact]
    public void An_mdfc_land_face_is_profiled_apart_from_its_spell_face()
        => Assert.Equal(
            ["", "develops tap={R} activated"],
            DescribeFaces("Shatterskull Smashing // Shatterskull, the Hammer Pass", "Sorcery // Land", ["R"],
                new("Shatterskull Smashing", "Sorcery", "Shatterskull Smashing deals X damage divided as you choose among up to two target creatures and/or planeswalkers. If X is 6 or more, Shatterskull Smashing deals twice X damage divided as you choose among them instead.", null),
                new("Shatterskull, the Hammer Pass", "Land", "As this land enters, you may pay 3 life. If you don't, it enters tapped.\n{T}: Add {R}.", ["R"])));

    [Fact]
    public void An_mdfc_land_face_keeps_its_profile_when_its_spell_face_is_unprofiled()
        => Assert.Equal(
            ["unprofiled(Put any number of cards from your hand on the bottom of your library, then draw that many cards plus one)", "develops tap={R} activated"],
            DescribeFaces("Valakut Awakening // Valakut Stoneforge", "Instant // Land", ["R"],
                new("Valakut Awakening", "Instant", "Put any number of cards from your hand on the bottom of your library, then draw that many cards plus one.", null),
                new("Valakut Stoneforge", "Land", "This land enters tapped.\n{T}: Add {R}.", ["R"])));

    [Fact]
    public void A_creature_face_taps_by_its_text_and_a_land_face_by_its_colors()
        => Assert.Equal(
            ["develops tap={G} activated", "develops tap={G} activated"],
            DescribeFaces("Tangled Florahedron // Tangled Vale", "Creature — Elemental // Land", ["G"],
                new("Tangled Florahedron", "Creature — Elemental", "{T}: Add {G}.", null),
                new("Tangled Vale", "Land", "This land enters tapped.\n{T}: Add {G}.", ["G"])));

    // A transform card's face rows carry no colors: its land face uses the
    // card's produced_mana. The front face names itself.
    [Fact]
    public void A_transform_land_face_uses_the_card_colors()
        => Assert.Equal(
            ["", "develops tap={U} activated"],
            DescribeFaces("Search for Azcanta // Azcanta, the Sunken Ruin", "Legendary Enchantment // Legendary Land", ["U"],
                new("Search for Azcanta", "Legendary Enchantment", "At the beginning of your upkeep, surveil 1. Then if you have seven or more cards in your graveyard, you may transform Search for Azcanta. (Look at the top card of your library. You may put that card into your graveyard.)", null),
                new("Azcanta, the Sunken Ruin", "Legendary Land", "(Transforms from Search for Azcanta.)\n{T}: Add {U}.\n{2}{U}, {T}: Look at the top four cards of your library. You may reveal a noncreature, nonland card from among them and put it into your hand. Put the rest on the bottom of your library in any order.", null)));

    [Fact]
    public void An_adventure_face_casts_its_own_tutor()
        => Assert.Equal(
            ["", "develops tutor=battlefield/cast"],
            DescribeFaces("Beanstalk Giant // Fertile Footsteps", "Creature — Giant // Sorcery — Adventure", null,
                new("Beanstalk Giant", "Creature — Giant", "Beanstalk Giant's power and toughness are each equal to the number of lands you control.", null),
                new("Fertile Footsteps", "Sorcery — Adventure", "Search your library for a basic land card, put it onto the battlefield, then shuffle. (Then exile this card. You may cast the creature later from exile.)", null)));

    // The card's produced_mana comes from its front face's trigger, which
    // is outside the family: a nonland face gets no produced colors.
    [Fact]
    public void A_nonland_face_gets_no_produced_colors()
        => Assert.Equal(
            ["unprofiled(Whenever you cast a spell, add {R})", "activated"],
            DescribeFaces("Birgi, God of Storytelling // Harnfel, Horn of Bounty", "Legendary Creature — God // Legendary Artifact", ["R"],
                new("Birgi, God of Storytelling", "Legendary Creature — God", "Whenever you cast a spell, add {R}. Until end of turn, you don't lose this mana as steps and phases end.\nCreatures you control can boast twice during each of your turns rather than once.", null),
                new("Harnfel, Horn of Bounty", "Legendary Artifact", "Discard a card: Exile the top two cards of your library. You may play those cards this turn.", null)));

    [Fact]
    public void A_card_without_face_rows_is_its_own_front_face()
    {
        var face = Assert.Single(CardFeatureDerivation.FacesOf("Sol Ring", "Artifact", "{T}: Add {C}{C}.", ["C"], []));
        Assert.Equal("Sol Ring", face.Name);
        Assert.Equal("develops tap={C}{C} activated", ProfileText.Of(CardFeatureDerivation.Derive(face).Profile));
    }

    [Fact]
    public void Card_without_face_rows_ignores_colors_of_tokens_for_another_player()
    {
        var face = Assert.Single(CardFeatureDerivation.FacesOf(
            "An Offer You Can't Refuse", "Instant", "Counter target noncreature spell. Its controller creates two Treasure tokens.", ["B", "G", "R", "U", "W"], []));
        Assert.Equal("counter=spell/not=Creature", ProfileText.Of(CardFeatureDerivation.Derive(face).Profile));
    }

    /// <summary>One face from its type line, Oracle text and Scryfall
    /// produced_mana ("B,G"; empty for none), as one profile line.</summary>
    private static string Describe(string name, string typeLine, string oracleText, string produced)
    {
        var result = CardFeatureDerivation.Derive(new CardFeatureDerivation.Face(
            name,
            CardDerivations.ParseTypeLine(typeLine).CardTypes,
            oracleText,
            produced.Length > 0 ? produced.Split(',') : null));
        return ProfileText.Of(result);
    }

    /// <summary>Each face's profile line, in face order, for a card whose
    /// top-level Oracle text is null (every multi-face layout).</summary>
    private static string[] DescribeFaces(string name, string typeLine, string[]? producedMana, params CardFeatureDerivation.FaceRow[] faces)
        =>
        [
            .. CardFeatureDerivation.FacesOf(name, typeLine, null, producedMana, faces)
                .Select(CardFeatureDerivation.Derive)
                .Select(ProfileText.Of),
        ];
}
