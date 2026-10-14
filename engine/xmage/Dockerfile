# syntax=docker/dockerfile:1.7
# MTG Archive XMage sidecar (ADR 0011). Build context: engine/xmage.
#   src                    sparse clone of the tag and commit pinned in XMAGE_TAG
#   xmage                  patches/*.patch applied in file-name order; XMage modules installed into /m2
#   shim-build             the shim jar and its runtime classpath in /opt/xmage/lib
#   dbinit                 the H2 card database, baked into /opt/xmage/db
#   shim-test              mvn test against that database: a failing test fails the image
#   selftest               the determinism self-test (selftest.sh), tier 2 and interactive: a difference fails the image
#   runtime (default)      JRE, jars and database; listens on 7070
#   determinism-only       runtime with patch 0001 alone (0002 reversed in the core jar)
# The patch series is regenerated with engine/xmage/tools/make-patches.sh; the steps are in
# engine/xmage/README.md.

FROM alpine:3.20 AS src
RUN apk add --no-cache git
COPY XMAGE_TAG /XMAGE_TAG
# Sparse, blob-less clone: engine, cards, AI player, commander game types, and every pom for the reactor.
RUN . /XMAGE_TAG \
 && git clone --filter=blob:none --sparse --depth 1 --branch "$tag" https://github.com/magefree/mage.git /src/mage \
 && cd /src/mage \
 && git sparse-checkout set --no-cone '/*' '!/*/' '/**/pom.xml' '/repository/' '/Mage/' '/Mage.Common/' '/Mage.Sets/' \
      '/Mage.Server.Plugins/Mage.Player.AI/' '/Mage.Server.Plugins/Mage.Game.CommanderDuel/' \
      '/Mage.Server.Plugins/Mage.Game.CommanderFreeForAll/' \
 && test "$(git rev-parse HEAD)" = "$commit"

FROM maven:3.9.9-eclipse-temurin-17 AS xmage
COPY --from=src /src /src
COPY patches /patches
WORKDIR /src/mage
ENV MAVEN_OPTS="-Xmx3g"
# /m2 is a normal directory, not a cache mount: the shim stages and the GitHub Actions layer cache need it.
RUN for p in /patches/*.patch; do git apply --verbose "$p" || exit 1; done
RUN mvn -B -q -Dmaven.repo.local=/m2 -DskipTests -Djacoco.skip=true \
      -pl Mage.Server.Plugins/Mage.Player.AI,Mage.Server.Plugins/Mage.Game.CommanderDuel -am install

FROM xmage AS shim-build
COPY log4j.properties /build/log4j.properties
COPY protocol /build/protocol
COPY shim /build/shim
RUN mvn -B -q -Dmaven.repo.local=/m2 -f /build/shim/pom.xml -DskipTests package \
 && mkdir -p /opt/xmage/lib \
 && cp /build/shim/target/lib/*.jar /build/shim/target/mtg-archive-xmage-shim.jar /opt/xmage/lib/

# Card database: built once at image build time, so the sidecar starts in seconds.
FROM shim-build AS dbinit
COPY log4j.properties /opt/xmage/log4j.properties
WORKDIR /opt/xmage
RUN java -Xmx2g -Dlog4j.configuration=file:/opt/xmage/log4j.properties -cp '/opt/xmage/lib/*' mtgarchive.xmage.DbInit \
      > /opt/xmage/dbinit.txt \
 && cat /opt/xmage/dbinit.txt \
 && grep -q 'scan_errors=0' /opt/xmage/dbinit.txt

FROM dbinit AS shim-test
# The tests open ./db/cards.h2 in the module directory; they get their own copy.
RUN cp -r /opt/xmage/db /build/shim/db \
 && mvn -B -Dmaven.repo.local=/m2 -f /build/shim/pom.xml test \
 && touch /opt/xmage/shim-test.ok

FROM shim-test AS selftest
COPY selftest.sh /opt/xmage/selftest.sh
RUN sh /opt/xmage/selftest.sh 101 20 121 20 > /opt/xmage/selftest.txt; status=$?; cat /opt/xmage/selftest.txt; exit $status

FROM eclipse-temurin:17-jre AS runtime-base
COPY --from=selftest /opt/xmage/lib /opt/xmage/lib
COPY --from=selftest /opt/xmage/db /opt/xmage/db
COPY --from=selftest /opt/xmage/dbinit.txt /opt/xmage/shim-test.ok /opt/xmage/selftest.txt /opt/xmage/
COPY XMAGE_TAG log4j.properties run.sh health.sh /opt/xmage/
COPY patches /opt/xmage/patches
# The version hashes the patch series and the shim sources (Version.java), so hello names the build.
COPY shim/pom.xml /opt/xmage/shim/pom.xml
COPY shim/src/main /opt/xmage/shim/src/main
WORKDIR /opt/xmage
EXPOSE 7070
ENTRYPOINT ["sh", "/opt/xmage/run.sh"]

# Patch 0001 alone: reverse 0002 in the core module and rebuild only its jar. 0002 changes no signature.
FROM xmage AS xmage-determinism-only
RUN git apply -R --verbose /patches/0002-game-copies.patch \
 && mvn -B -q -o -Dmaven.repo.local=/m2 -DskipTests -Djacoco.skip=true -pl Mage package \
 && mkdir -p /out && cp Mage/target/mage-1.4.62.jar /out/

FROM runtime-base AS determinism-only
COPY --from=xmage-determinism-only /out/mage-1.4.62.jar /opt/xmage/lib/mage-1.4.62.jar
RUN rm /opt/xmage/patches/0002-game-copies.patch

FROM runtime-base AS runtime
