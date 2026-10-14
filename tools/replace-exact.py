"""Exact-match replacement that keeps a file's line endings.

Usage: python tools/replace-exact.py edits.json
edits.json: [{"path": "...", "old": "...", "new": "..."}]
Each `old` must match exactly once, using LF; CRLF files are converted back.
"""
import json
import sys

for e in json.load(open(sys.argv[1], encoding="utf-8")):
    with open(e["path"], encoding="utf-8", newline="") as f:
        text = f.read()
    crlf = "\r\n" in text
    text = text.replace("\r\n", "\n")
    n = text.count(e["old"])
    assert n == 1, f'{e["path"]}: {n} matches for {e["old"][:60]!r}'
    text = text.replace(e["old"], e["new"])
    if crlf:
        text = text.replace("\n", "\r\n")
    with open(e["path"], "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print("edited", e["path"])
