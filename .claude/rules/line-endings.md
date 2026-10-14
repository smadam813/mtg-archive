---
paths:
  - "**/*.md"
  - "**/*.cs"
---

# Line endings

- Git for Windows defaults to `core.autocrlf=true`, so a Windows working tree is CRLF and the index LF. The repo does not set it; the devcontainer, CI, and WSL check out LF.
- `.gitattributes` keeps `*.sh`, `Dockerfile`, the parity JSON, and the XMage patches LF or byte-exact on every host. Do not convert them.
- For an exact multi-line replacement, run `python tools/replace-exact.py edits.json`. It keeps each file's endings and fails unless `old` matches once.
