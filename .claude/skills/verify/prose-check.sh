#!/usr/bin/env bash
# Advisory prose check (CLAUDE.md writing style): report sentences of more than
# 25 words in the lines this branch adds to docs, the glossary and the ADRs.
# It exits 0 always; the verdict is the reader's.
# Usage: prose-check.sh [base-ref, default origin/main, else main]
set -u
base="${1:-origin/main}"
git rev-parse --verify -q "$base" >/dev/null || base=main
fork=$(git merge-base HEAD "$base")
out=$(git diff -U0 "$fork" -- 'docs/*.md' GLOSSARY.md CLAUDE.md 'eval/*README.md' | tr -d '\r' | awk -v limit=25 '
  /^\+\+\+ / { file = substr($0, 7); next }
  /^@@/ { match($0, /\+[0-9]+/); ln = substr($0, RSTART + 1, RLENGTH - 1) - 1; next }
  /^\+/ {
    ln++
    line = substr($0, 2)
    if (line ~ /^(```|\||#|---)/) next
    gsub(/`[^`]*`/, "X", line)
    n = split(line, parts, /[.!?]+([ ]|$)/)
    for (i = 1; i <= n; i++) {
      w = split(parts[i], words, /[ ]+/)
      if (w > limit) printf "%s:%d: %d words: %.70s...\n", file, ln, w, parts[i]
    }
  }')
if [ -n "$out" ]; then echo "$out"; else echo "prose-check: no added sentence over 25 words"; fi
exit 0
