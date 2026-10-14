package mtgarchive.xmage;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

class VersionTest {

    @Test
    void versionIsTheTagAndTheFirstEightHexOfThePatchAndShimHash(@TempDir Path dir) throws Exception {
        Path tag = tag(dir);
        Path patches = patches(dir);
        Path shim = shim(dir.resolve("shim"), "J\r\nK");

        // sha256("A" "B" "pom.xml\0P\0" "src/main/java/X.java\0J\nK\0" "src/main/resources/r.txt\0R\0")
        // starts 85629c13: patches in file-name order, then shim files in path order, CRLF read as LF.
        assertEquals("xmage-1.4.62V1+85629c13", Version.of(tag, patches, shim));
    }

    @Test
    void aShimSourceChangeChangesTheVersion(@TempDir Path dir) throws Exception {
        Path tag = tag(dir);
        Path patches = patches(dir);
        Path before = shim(dir.resolve("before"), "J\nK");
        Path after = shim(dir.resolve("after"), "J\nL");

        assertNotEquals(Version.of(tag, patches, before), Version.of(tag, patches, after));
    }

    @Test
    void aNewShimFileChangesTheVersion(@TempDir Path dir) throws Exception {
        Path tag = tag(dir);
        Path patches = patches(dir);
        Path shim = shim(dir.resolve("shim"), "J\nK");
        String before = Version.of(tag, patches, shim);
        Files.writeString(shim.resolve("src/main/java/Y.java"), "");

        assertNotEquals(before, Version.of(tag, patches, shim));
    }

    @Test
    void twoCopiesOfOneTreeGiveOneVersion(@TempDir Path dir) throws Exception {
        Path tag = tag(dir);
        Path patches = patches(dir);
        // a Linux checkout (LF) and a Windows checkout (CRLF) of the same tree
        Path lf = shim(dir.resolve("lf"), "J\nK");
        Path crlf = shim(dir.resolve("crlf"), "J\r\nK");

        assertEquals(Version.of(tag, patches, lf), Version.of(tag, patches, lf));
        assertEquals(Version.of(tag, patches, lf), Version.of(tag, patches, crlf));
    }

    @Test
    void theShimInputsAreThePomAndEveryMainSourceOfThisModule() throws Exception {
        // mvn test runs in the shim module directory
        Path module = Path.of("").toAbsolutePath();
        List<String> inputs = Version.shimFiles(module);

        List<String> expected;
        try (Stream<Path> walk = Files.walk(module.resolve("src/main"))) {
            expected = Stream.concat(Stream.of("pom.xml"), walk.filter(Files::isRegularFile)
                            .map(f -> module.relativize(f).toString().replace('\\', '/')))
                    .sorted()
                    .toList();
        }
        assertEquals(expected, inputs);
        assertTrue(inputs.contains("src/main/java/mtgarchive/xmage/PilotPlayer.java"));
        assertTrue(inputs.contains("src/main/java/mtgarchive/xmage/Version.java"));
        assertTrue(inputs.stream().noneMatch(p -> p.startsWith("src/test/")));
    }

    @Test
    void aShimDirectoryWithoutAPomIsRefused(@TempDir Path dir) throws Exception {
        Path tag = tag(dir);
        Path patches = patches(dir);
        Path shim = shim(dir.resolve("shim"), "J\nK");
        Files.delete(shim.resolve("pom.xml"));

        assertThrows(IllegalArgumentException.class, () -> Version.of(tag, patches, shim));
    }

    @Test
    void aTagFileWithoutATagLineIsRefused(@TempDir Path dir) throws Exception {
        Path tag = dir.resolve("XMAGE_TAG");
        Files.writeString(tag, "commit=eb69d05b88142b5c4eec4869bcb23a0fec13c091\n");
        Path patches = Files.createDirectory(dir.resolve("patches"));
        Files.writeString(patches.resolve("0001-a.patch"), "A");
        Path shim = shim(dir.resolve("shim"), "J\nK");

        assertThrows(IllegalArgumentException.class, () -> Version.of(tag, patches, shim));
    }

    private static Path tag(Path dir) throws Exception {
        Path tag = dir.resolve("XMAGE_TAG");
        Files.writeString(tag, "tag=xmage_1.4.62V1\ncommit=eb69d05b88142b5c4eec4869bcb23a0fec13c091\n");
        return tag;
    }

    private static Path patches(Path dir) throws Exception {
        Path patches = Files.createDirectory(dir.resolve("patches"));
        // file-name order, not creation order: "A" then "B"
        Files.writeString(patches.resolve("0002-b.patch"), "B");
        Files.writeString(patches.resolve("0001-a.patch"), "A");
        Files.writeString(patches.resolve("README.txt"), "not a patch");
        return patches;
    }

    private static Path shim(Path shim, String javaSource) throws Exception {
        Files.createDirectories(shim.resolve("src/main/java"));
        Files.createDirectories(shim.resolve("src/main/resources"));
        Files.createDirectories(shim.resolve("src/test/java"));
        // written out of path order on purpose
        Files.writeString(shim.resolve("src/main/resources/r.txt"), "R");
        Files.writeString(shim.resolve("src/main/java/X.java"), javaSource);
        Files.writeString(shim.resolve("pom.xml"), "P");
        Files.writeString(shim.resolve("src/test/java/T.java"), "not an input");
        return shim;
    }
}
