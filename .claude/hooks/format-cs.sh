#!/bin/sh
# PostToolUse (Write|Edit): whitespace-format the touched C# file.
# dotnet format --include matches only paths relative to the solution. git
# turns the tool's absolute path (Windows or POSIX, any case, with ./ or ..
# segments) into a repo-relative one; a plain prefix strip misses those cases
# and then formats nothing. Outside the repo git prints nothing: skip.
f=$(jq -r '.tool_input.file_path // empty')
case "$f" in
  *.cs) ;;
  *) exit 0 ;;
esac
cd "$CLAUDE_PROJECT_DIR" || exit 0
rel=$(git ls-files --full-name --cached --others -- "$f" 2>/dev/null | head -1)
[ -n "$rel" ] || exit 0
case "$rel" in
  src/MtgArchive/Migrations/*) exit 0 ;;
esac
dotnet format whitespace MtgArchive.sln --include "$rel" -v q
