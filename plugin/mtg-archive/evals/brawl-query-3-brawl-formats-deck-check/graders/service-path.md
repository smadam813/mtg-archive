---
type: tool_used
tool: Bash
input_match: '(MTG_ARCHIVE_URL[^/\s]*|127\.0\.0\.1:8080\}?|localhost:8080\}?)(\\"|")?/(deck-verdict)'
arm: both
---

A shell call reaches `/deck-verdict` on the service address.
