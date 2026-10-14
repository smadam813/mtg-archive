---
type: tool_used
tool: Bash
input_match: '(MTG_ARCHIVE_URL[^/\s]*|127\.0\.0\.1:8080\}?|localhost:8080\}?)(\\"|")?/(cards/search|cards/semantic-search|tier1-report|simulate)'
arm: both
---

A shell call reaches one of `/cards/search`, `/cards/semantic-search`, `/tier1-report`, or `/simulate` on the service address.
