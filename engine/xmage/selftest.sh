#!/bin/sh
# Determinism self-test (spec 6.3), modeled on the spike's worker.sh "det" command.
# Usage: selftest.sh SEED GAMES [ISEED IGAMES]. Runs SelfTest four times on a copy of the card
# database and compares the per-seed digests: a fresh JVM, a second fresh JVM, 4 game threads, and
# other JVM flags (serial GC, identity hash mode 2). Each run also replays its seeds in the same JVM.
# With ISEED and IGAMES it runs the interactive pass the same four ways: the scripted
# opponent opposes the first cast from turn 2 with Counterspell, the end of turn 3 with Atomize and
# the end of turn 4 with Rout.
# Every game must end at the horizon or on a loss: a game that ends with engine_error proves
# nothing about play, even when its digest repeats.
# Prints one SELFTEST line per check and exits 1 when any check differs.
set -eu
seed=$1
games=$2
iseed=${3:-}
igames=${4:-}
work=$(mktemp -d)
cp -r /opt/xmage/db "$work/db"
cd "$work"

run() { # $1 = extra JVM flags, $2 = game threads, $3 = output file, $4 = seed, $5 = games, $6 = interactive
  # shellcheck disable=SC2086
  if ! java -Xmx3g $1 -Dlog4j.configuration=file:/opt/xmage/log4j.properties -cp '/opt/xmage/lib/*' \
      mtgarchive.xmage.SelfTest --seed "$4" --games "$5" --threads "$2" --interactive "$6" > "$3.out"; then
    # SelfTest exits 1 when its same-JVM replay differs: show its lines before failing.
    echo "SELFTEST run=$3 failed"
    grep -E '^(SELFTEST|GAME) ' "$3.out" || true
    exit 1
  fi
  grep '^GAME ' "$3.out" > "$3"
  test "$(wc -l < "$3")" -eq "$5"
}

failed=0
check() { # $1 = check name, $2 = reference file, $3 = file to compare with it, $4 = games
  if cmp -s "$2" "$3"; then
    echo "SELFTEST check=$1 identical=true games=$4"
  else
    echo "SELFTEST check=$1 identical=false games=$4"
    diff "$2" "$3" | head -n 6
    failed=1
  fi
}
played() { # $1 = check name, $2 = file, $3 = games
  errors=$(grep -c ' engine_error ' "$2" || true)
  if [ "$errors" -eq 0 ]; then
    echo "SELFTEST check=$1 engine_errors=0 games=$3"
  else
    echo "SELFTEST check=$1 engine_errors=$errors games=$3"
    grep ' engine_error ' "$2" | head -n 6
    failed=1
  fi
}

run "" 1 a "$seed" "$games" false
run "" 1 b "$seed" "$games" false
run "" 4 c "$seed" "$games" false
run "-XX:+UseSerialGC -XX:+UnlockExperimentalVMOptions -XX:hashCode=2" 1 d "$seed" "$games" false
check fresh_pair a b "$games"
check threads a c "$games"
check jvm_flags a d "$games"
played played a "$games"

if [ -n "$iseed" ]; then
  run "" 1 ia "$iseed" "$igames" true
  run "" 1 ib "$iseed" "$igames" true
  run "" 4 ic "$iseed" "$igames" true
  run "-XX:+UseSerialGC -XX:+UnlockExperimentalVMOptions -XX:hashCode=2" 1 id "$iseed" "$igames" true
  check interactive_fresh_pair ia ib "$igames"
  check interactive_threads ia ic "$igames"
  check interactive_jvm_flags ia id "$igames"
  played interactive_played ia "$igames"
fi
cat a
if [ -n "$iseed" ]; then
  cat ia
fi
exit $failed
