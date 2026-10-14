# Acceptance records

One record per acceptance query. A human runs a fresh session and grades the answer.

## Before the session

Run `bash eval/acceptance/preflight.sh`. It checks that the installed plugin matches `plugin/mtg-archive` and that the `service` and `xmage` images are newer than their sources. Copy its output into the record's Environment line. Fix a failure first.

## After the session

- Save the question and answer word for word as `<record>/transcript.md`.
- Save each request the session sent as `<record>/request-*.json`.
- Replay with the saved request files, not with defaults. A default `turn_horizon` of 10 changes the event rates.
