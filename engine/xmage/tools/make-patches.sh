#!/bin/sh
# Regenerate engine/xmage/patches/ at the tag in XMAGE_TAG, in a container.
# Usage: engine/xmage/tools/make-patches.sh [<tag> <commit>]
# On Git Bash the MSYS_NO_PATHCONV setting below keeps the sparse-checkout patterns intact.
set -eu
export MSYS_NO_PATHCONV=1
here=$(cd "$(dirname "$0")/.." && { pwd -W 2>/dev/null || pwd; })
docker run --rm \
  -v "$here/XMAGE_TAG:/xmage/XMAGE_TAG:ro" \
  -v "$here/tools:/xmage/tools:ro" \
  -v "$here/patches:/xmage/patches" \
  maven:3.9.9-eclipse-temurin-17 sh /xmage/tools/regenerate-patches.sh "$@"
