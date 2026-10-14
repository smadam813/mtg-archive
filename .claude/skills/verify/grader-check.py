#!/usr/bin/env python3
"""Check the eval grader regexes (plugin/mtg-archive/evals/*/graders/*.md).

Every `input_match` must compile. A grader listed in evals/grader-samples.json
must also match each "match" sample and none of the "nomatch" samples. A shell
heredoc can drop a backslash from a regex without an error; the samples catch it.
Run from the repo root. Exit 1 on any failure.
"""
import json
import pathlib
import re
import sys

root = pathlib.Path("plugin/mtg-archive/evals")
samples = json.loads((root / "grader-samples.json").read_text(encoding="utf-8"))
failed = 0
seen = set()

for grader in sorted(root.glob("*/graders/*.md")):
    key = f"{grader.parent.parent.name}/{grader.stem}"
    found = re.search(r"^input_match: '(.*)'\s*$", grader.read_text(encoding="utf-8"), re.M)
    if not found:
        continue
    try:
        pattern = re.compile(found.group(1).replace("''", "'"))
    except re.error as err:
        print(f"{key}: does not compile: {err}")
        failed += 1
        continue
    case = samples.get(key)
    if case is None:
        continue
    seen.add(key)
    for text in case["match"]:
        if not pattern.search(text):
            print(f"{key}: should match {text!r}")
            failed += 1
    for text in case["nomatch"]:
        if pattern.search(text):
            print(f"{key}: should not match {text!r}")
            failed += 1

for key in sorted(set(samples) - seen):
    print(f"{key}: samples name a grader that does not exist")
    failed += 1

print("grader-check: all grader regexes pass" if not failed else f"grader-check: {failed} failure(s)")
sys.exit(1 if failed else 0)
