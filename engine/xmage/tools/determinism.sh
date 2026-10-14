#!/bin/sh
# Determinism patch for XMage, applied to a git checkout at $1.
# 1. RandomUtil: one Random per thread, plus a seeded object-ID stream (newUUID).
# 2. Every non-static UUID.randomUUID() in game code calls RandomUtil.newUUID() instead.
#    Static fields keep UUID.randomUUID(): class init order would otherwise shift the ID stream.
# 3. The two unseeded Collections.shuffle calls in PlayerImpl use RandomUtil.getRandom().
# 4. Deck.getMaindeckCards collected into a HashSet of cards. Card has no hashCode(), so the
#    library order before the shuffle followed JVM identity hashes. Keep deck order instead.
# 5. ComputerPlayer scores mana producers in a HashMap keyed by permanent (identity hash), then
#    sorts by score. Ties kept identity-hash order, so the land it taps varied between JVM runs.
# 6. RandomUtil.randomFromCollection sorts an unordered collection by object ID before it picks
#    (random discard, seek): callers pass HashSets of cards, which iterate in identity-hash order.
set -eu
cd "$1"
here=$(dirname "$0")

cp "$here/RandomUtil.java" Mage/src/main/java/mage/util/RandomUtil.java

grep -rl --include='*.java' 'UUID\.randomUUID()' \
    Mage/src/main/java Mage.Sets/src Mage.Server.Plugins/Mage.Player.AI/src \
    Mage.Server.Plugins/Mage.Game.CommanderDuel/src Mage.Server.Plugins/Mage.Game.CommanderFreeForAll/src \
  | grep -v 'mage/util/RandomUtil.java' \
  | while read -r f; do
      sed -i '/static/!s/java\.util\.UUID\.randomUUID()/mage.util.RandomUtil.newUUID()/g; /static/!s/\bUUID\.randomUUID()/mage.util.RandomUtil.newUUID()/g' "$f"
    done

sed -i 's/Collections\.shuffle(ids);/Collections.shuffle(ids, RandomUtil.getRandom());/' \
    Mage/src/main/java/mage/players/PlayerImpl.java

sed -i '/public Set<Card> getMaindeckCards()/,/;/ s/Collectors\.toSet()/Collectors.toCollection(LinkedHashSet::new)/' \
    Mage/src/main/java/mage/cards/decks/Deck.java

sed -i 's/Map<MageObject, Integer> scored = new HashMap<>();/Map<MageObject, Integer> scored = new LinkedHashMap<>();/' \
    Mage.Server.Plugins/Mage.Player.AI/src/mage/player/ai/ComputerPlayer.java

echo "patch: files and lines changed"
git diff --shortstat
git diff --stat | tail -n 60
echo "patch: remaining UUID.randomUUID() call sites (static or excluded)"
grep -rn --include='*.java' 'UUID\.randomUUID()' Mage/src/main/java Mage.Sets/src Mage.Server.Plugins/Mage.Player.AI/src | grep -v RandomUtil.java || true
