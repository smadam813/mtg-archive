#!/bin/sh
# PreToolUse (Write|Edit): EF migrations are generated, never hand-edited.
# The one exception is a user-approved migrationBuilder.Sql DDL call in a
# migration's own file (/new-migration step 4), so that file asks, not denies.
deny='Migrations are generated, never hand-edited. Change the model and run /new-migration (dotnet ef migrations add) instead.'
ask='Migrations are generated. The only allowed hand edit is a migrationBuilder.Sql DDL call the user approved (/new-migration step 4). Otherwise change the model and run /new-migration.'
decide() {
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"%s","permissionDecisionReason":"%s"}}' "$1" "$2"
}
in=$(cat)
if ! command -v jq >/dev/null 2>&1; then
  # No jq: fail closed. Any mention of the folder asks.
  case "$in" in
    *MtgArchive*Migrations*) decide ask "$ask" ;;
  esac
  exit 0
fi
f=$(printf '%s' "$in" | jq -r '(.tool_input.file_path // empty) | split([92] | implode) | join("/")')
case "$f" in
  */src/MtgArchive/Migrations/*.Designer.cs|*/src/MtgArchive/Migrations/*ModelSnapshot.cs) decide deny "$deny" ;;
  */src/MtgArchive/Migrations/*) decide ask "$ask" ;;
esac
