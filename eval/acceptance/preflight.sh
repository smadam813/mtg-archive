#!/usr/bin/env bash
# Run before an acceptance session. Prints the Environment facts for the
# record and exits 1 when the plugin cache or an image is older than the repo.
set -u
cd "$(git rev-parse --show-toplevel)"
bad=0

installed="$HOME/.claude/plugins/installed_plugins.json"
py=$(command -v python3 || command -v python)
sha=$("$py" -c "import json,sys;p=json.load(open(sys.argv[1]))['plugins'].get('mtg-archive@mtg-archive',[]);print(p[0].get('gitCommitSha','') if p else '')" "$installed" 2>/dev/null)
if [ -z "$sha" ]; then
  echo "plugin: not installed"; bad=1
elif git diff --quiet "$sha" HEAD -- plugin/mtg-archive 2>/dev/null; then
  echo "plugin: ${sha:0:8}, same content as plugin/mtg-archive at HEAD"
else
  echo "plugin: ${sha:0:8} DIFFERS from plugin/mtg-archive at HEAD (claude plugin marketplace update; claude plugin update mtg-archive@mtg-archive)"; bad=1
fi

for pair in "service:src/MtgArchive" "xmage:engine/xmage"; do
  svc=${pair%%:*}; paths=${pair#*:}
  created=$(docker image inspect -f '{{.Created}}' "mtg-archive-$svc" 2>/dev/null)
  if [ -z "$created" ]; then echo "$svc image: none"; bad=1; continue; fi
  built=$(date -d "$created" +%s)
  # shellcheck disable=SC2086
  changed=$(git log -1 --format=%ct -- $paths)
  if [ "$built" -ge "$changed" ]; then echo "$svc image: current"; else echo "$svc image: OLDER than the last change under $paths (docker compose -p mtg-archive -f compose.yaml -f compose.gpu.yml up -d --build $svc)"; bad=1; fi
done
exit $bad
