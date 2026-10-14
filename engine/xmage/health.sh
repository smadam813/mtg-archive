#!/bin/bash
# Compose healthcheck: send a protocol hello on a new connection and check the reply.
# An open port alone is not enough: the port stays open after a heap error ends the accept loop.
# The reply is one flat JSON object, so the read stops at its closing brace; bash drops the NUL
# bytes of the length prefix. XMAGE_PORT (default 7070) names the port, as for the shim. Exits
# non-zero when the connect fails or no hello reply arrives in 2 s.
set -eu
exec 3<>"/dev/tcp/127.0.0.1/${XMAGE_PORT:-7070}"
# The frame: a 4-byte big-endian length, then the JSON.
hello='{"type":"hello","protocol":2}'
printf "$(printf '\\x%02x' 0 0 0 ${#hello})%s" "$hello" >&3
IFS= read -r -t 2 -d '}' reply <&3
[[ $reply == *'"type":"hello"'* && $reply == *'"engine":"xmage"'* ]]
