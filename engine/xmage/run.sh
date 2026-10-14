#!/bin/sh
# XMage sidecar entry point. XMAGE_THREADS (default 24) caps the games that play at once.
# JAVA_OPTS replaces the JVM flags. By default they follow the spike's tuning for T game threads:
# heap 1024 MB + 160 MB per thread, 70% of it young generation, one GC thread per 3 game threads.
# A heap error ends the JVM: compose then restarts the container. If JAVA_OPTS
# replaces the defaults, keep -XX:+ExitOnOutOfMemoryError.
# XMAGE_SWITCHES adds system properties, for example -Dmtgarchive.copies.oneCopy=false.
set -eu
t=${XMAGE_THREADS:-24}
if [ -z "${JAVA_OPTS:-}" ]; then
  heap=$((1024 + 160 * t))
  young=$((heap * 7 / 10))
  gct=$((t / 3))
  if [ "$gct" -lt 2 ]; then gct=2; fi
  cic=2
  if [ "$t" -ge 16 ]; then cic=4; fi
  JAVA_OPTS="-Xms${heap}m -Xmx${heap}m -Xmn${young}m -XX:+UseParallelGC -XX:ParallelGCThreads=$gct -XX:CICompilerCount=$cic -XX:+ExitOnOutOfMemoryError"
fi
cd /opt/xmage
# shellcheck disable=SC2086
exec java $JAVA_OPTS ${XMAGE_SWITCHES:-} -Dlog4j.configuration=file:/opt/xmage/log4j.properties \
  -cp '/opt/xmage/lib/*' mtgarchive.xmage.Main
