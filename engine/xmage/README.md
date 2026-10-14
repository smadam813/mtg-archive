# XMage sidecar

The sidecar runs a patched XMage (ADR 0011). `XMAGE_TAG` pins the release tag and its commit. `patches/` holds the patch series. The image build applies the patches in file-name order.

The XMage source is not on disk; the image build clones it. To read one XMage file at the pinned commit, fetch it from GitHub. Do not search the disk for it.

```bash
gh api "repos/magefree/mage/contents/Mage/src/main/java/mage/game/GameImpl.java?ref=$(sed -n 's/^commit=//p' engine/xmage/XMAGE_TAG)" -H "Accept: application/vnd.github.raw"
```

- `0001-determinism.patch` makes XMage take its random numbers from the run seed. `tools/determinism.sh` generates it.
- `0002-game-copies.patch` holds the game-copies changes. A maintainer writes the changes by hand. `tools/make-patches.sh` rewrites the file from them (see the steps below).

## Edit patch 0002

The patch applies to the XMage source, which is not on disk. Edit it in a scratch checkout, and keep it outside the repo.

> **Caution.** On Git Bash, run `export MSYS_NO_PATHCONV=1` first. Without it the sparse-checkout patterns change, and the checkout holds no files.

1. Clone the pinned tag and set the same sparse patterns as the `src` stage of the `Dockerfile`.
2. Run `git apply` for `patches/0001-determinism.patch`, then `git add -A` and commit it in the scratch clone.
3. Run `git apply` for `patches/0002-game-copies.patch`, then edit the Java files.
4. Write the new patch: `git diff HEAD > engine/xmage/patches/0002-game-copies.patch`.
5. Run `tools/make-patches.sh`, then `git diff --exit-code engine/xmage/patches`. The diff is empty when the generator agrees.
6. Build the image with `docker build --target selftest`. The shim tests and the self-test run in that build.

Do not start more than two sidecars at once at the default 16 game threads. Five at once ran out of memory in Docker Desktop, and the dead sidecar gave wrong leak counts.

## Move to a new XMage tag

> **Caution.** On Git Bash, run Docker with `MSYS_NO_PATHCONV=1`. Without it, Docker rewrites the paths in the sparse-checkout patterns. `tools/make-patches.sh` sets it.

> **Caution.** The patch files must keep Unix line endings. `.gitattributes` has the rule `engine/xmage/patches/*.patch -text`. Write patches from inside the container or with `git show`. Do not write them from a Windows checkout.

1. Find the new tag and its commit on `magefree/mage`.
2. Write both into `XMAGE_TAG` as the lines `tag=...` and `commit=...`.
3. Do not change that format. The Dockerfile and `Version.of` read it.
4. Regenerate the patches. The command reads `XMAGE_TAG`, runs in a container and writes both patch files.

   ```bash
   engine/xmage/tools/make-patches.sh
   ```

   To try a tag without an edit to `XMAGE_TAG`, pass `<tag> <commit>` as arguments.
5. Read the output. The generator does these steps in order:
   1. It makes a sparse clone of the tag. It stops if `HEAD` is not the pinned commit.
   2. It runs `tools/determinism.sh` and writes the `git diff` as `0001`.
      It stops if an edit in that script changes nothing. Then update the matching pattern in `determinism.sh`, and regenerate.
   3. It applies the old `0002` on top of `0001` and writes the new `git diff` as `0002`.
   4. It resets the clone to the tag and runs `git apply --check` on both patches in order.
6. If the old `0002` does not apply, the generator exits with an error. The folder then holds the new `0001` and the old `0002`. Edit `0002` by hand and run step 4 again.
7. Change every other place that holds the tag:
   - `Dockerfile`: the `mage-1.4.xx.jar` names in the `xmage-determinism-only` and `determinism-only` stages (three names).
   - `shim/pom.xml`: `xmage.version` and the comment above it.
   - `shim/src/test/java/mtgarchive/xmage/VersionTest.java`: the tag, the commit and the expected hash.
   - `shim/src/test/java/mtgarchive/xmage/SchemaConformanceTest.java`: the version string.
   - `protocol/examples/valid/hello_reply.json` and `protocol/examples/invalid/hello_reply_bad_version.json`: the version string.
   - Do not edit the `xmage-1.4.xxVx+0123abcd` strings in `tests/MtgArchive.Tests`. They are test data. The comments in `PilotPlayer.java` and `Version.java` that name the tag are optional.
8. Run the checks that a tag move needs. `eval/parity/README.md` has the commands.
   1. Build the test image: `docker build --target runtime -t mtg-archive-xmage:ci engine/xmage`.
   2. Run `leak` and `determinism` on the candidate image.
   3. Run `parity --image <candidate>`.
   4. Run `parity-baseline --image mtg-archive-xmage`. It rewrites `eval/parity/baseline.json`. Build that image first with `docker compose build`.
   5. Run the throughput check. ADR 0011 warns that a tag move can use up the throughput margin.
   6. Run `/verify`. It builds and tests its own per-checkout image tag, not `mtg-archive-xmage:ci`.

## Check the generator

Run `engine/xmage/tools/make-patches.sh` at the current pin. Then run `git diff --exit-code engine/xmage/patches`. The diff is empty when the generator and the committed patches agree.
