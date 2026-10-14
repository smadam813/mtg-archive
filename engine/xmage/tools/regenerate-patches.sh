#!/bin/sh
# Regenerate the XMage patch series inside a container (see make-patches.sh and ../README.md).
#   /xmage/XMAGE_TAG   tag and commit pins (mounted read-only)
#   /xmage/tools       this folder (mounted read-only)
#   /xmage/patches     the patch folder; the script reads the old 0002 here and writes both new patches here
# Optional arguments: <tag> <commit>. They replace the values in XMAGE_TAG.
set -eu

tag=""
commit=""
. /xmage/XMAGE_TAG
if [ $# -ne 0 ] && [ $# -ne 2 ]; then
  echo "usage: make-patches.sh [<tag> <commit>]" >&2
  exit 2
fi
if [ $# -eq 2 ]; then
  tag=$1
  commit=$2
fi
if [ -z "$tag" ] || [ -z "$commit" ]; then
  echo "error: no tag or commit. Pass <tag> <commit> or set both in XMAGE_TAG." >&2
  exit 2
fi

mkdir -p /work
work=/work/mage
old=/work/old-0002.patch
cp /xmage/patches/0002-game-copies.patch "$old"

git config --global core.autocrlf false
git config --global user.email patches@example.invalid
git config --global user.name patches

echo "clone: $tag"
git clone --quiet --filter=blob:none --sparse --depth 1 --branch "$tag" https://github.com/magefree/mage.git "$work"
cd "$work"
# Keep these patterns equal to the src stage of the Dockerfile.
git sparse-checkout set --no-cone '/*' '!/*/' '/**/pom.xml' '/repository/' '/Mage/' '/Mage.Common/' '/Mage.Sets/' \
  '/Mage.Server.Plugins/Mage.Player.AI/' '/Mage.Server.Plugins/Mage.Game.CommanderDuel/' \
  '/Mage.Server.Plugins/Mage.Game.CommanderFreeForAll/'

head=$(git rev-parse HEAD)
if [ "$head" != "$commit" ]; then
  echo "error: tag $tag is at $head, not the pinned commit $commit." >&2
  exit 1
fi

echo "0001: determinism.sh"
sh /xmage/tools/determinism.sh "$work"
# determinism.sh uses sed, which exits 0 when a pattern no longer matches. Check that each edit landed.
need() { grep -q "$2" "$1" || { echo "error: determinism.sh made no change in $1 ($2)." >&2; exit 1; }; }
need Mage/src/main/java/mage/players/PlayerImpl.java 'Collections.shuffle(ids, RandomUtil.getRandom());'
need Mage/src/main/java/mage/cards/decks/Deck.java '.collect(Collectors.toCollection(LinkedHashSet::new));'
need Mage.Server.Plugins/Mage.Player.AI/src/mage/player/ai/ComputerPlayer.java 'Map<MageObject, Integer> scored = new LinkedHashMap<>();'
git add -N .
git diff > /xmage/patches/0001-determinism.patch
git add -A

echo "0002: old patch on top of 0001"
if ! git apply --verbose "$old"; then
  echo "error: the old 0002-game-copies.patch does not apply on 0001 at $tag." >&2
  echo "Resolve it by hand. The folder now holds a new 0001 and the old 0002." >&2
  exit 1
fi
git add -N .
git diff > /xmage/patches/0002-game-copies.patch

echo "check: apply both patches to a clean checkout of the tag"
git reset --quiet --hard "$head"
git clean --quiet -fd
for p in /xmage/patches/*.patch; do
  git apply --check "$p" || { echo "error: $p does not apply." >&2; exit 1; }
  git apply "$p"
done
# Give the files to the owner of the mounted folder, not root.
chown "$(stat -c %u:%g /xmage/patches)" /xmage/patches/*.patch 2>/dev/null || true
echo "done: $(ls /xmage/patches | tr '\n' ' ')"
