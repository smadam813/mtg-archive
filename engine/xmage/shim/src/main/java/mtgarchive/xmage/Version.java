package mtgarchive.xmage;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.Reader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HexFormat;
import java.util.List;
import java.util.Properties;
import java.util.stream.Stream;

/**
 * The engine version that hello reports: "xmage-1.4.62V1+" and the first 8 hex digits of one
 * SHA-256 over the engine's behavior inputs (roadmap, "Wire protocol"; changed in phase 3B):
 * <ol>
 *   <li>the patch files, byte for byte, in file-name order;</li>
 *   <li>the shim's pom.xml and every file under its src/main, in relative-path order. Each file
 *       adds its relative path ('/' separators), a NUL byte, its content with CRLF read as LF, and
 *       a NUL byte. A Windows checkout and a Linux checkout of one tree give one version.</li>
 * </ol>
 */
public final class Version {

    private Version() {
    }

    /**
     * @param xmageTagFile the XMAGE_TAG file ("tag=xmage_1.4.62V1" and "commit=..." lines)
     * @param patchDir     the directory that holds the *.patch series
     * @param shimDir      the shim module directory: pom.xml and src/main/**
     */
    public static String of(Path xmageTagFile, Path patchDir, Path shimDir) throws IOException {
        Properties pin = new Properties();
        try (Reader reader = Files.newBufferedReader(xmageTagFile)) {
            pin.load(reader);
        }
        String tag = pin.getProperty("tag");
        if (tag == null || !tag.startsWith("xmage_")) {
            throw new IllegalArgumentException(xmageTagFile + " needs a line tag=xmage_<version>");
        }
        List<Path> patches;
        try (Stream<Path> files = Files.list(patchDir)) {
            patches = files.filter(f -> f.getFileName().toString().endsWith(".patch"))
                    .sorted(Comparator.comparing(f -> f.getFileName().toString()))
                    .toList();
        }
        if (patches.isEmpty()) {
            throw new IllegalArgumentException(patchDir + " holds no *.patch file");
        }
        List<String> shimFiles = shimFiles(shimDir);
        try {
            MessageDigest sha = MessageDigest.getInstance("SHA-256");
            for (Path patch : patches) {
                sha.update(Files.readAllBytes(patch));
            }
            for (String relative : shimFiles) {
                sha.update(relative.getBytes(StandardCharsets.UTF_8));
                sha.update((byte) 0);
                sha.update(lf(Files.readAllBytes(shimDir.resolve(relative))));
                sha.update((byte) 0);
            }
            String hex = HexFormat.of().formatHex(sha.digest());
            return "xmage-" + tag.substring("xmage_".length()) + "+" + hex.substring(0, 8);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    /** The shim inputs, as '/'-separated paths relative to shimDir, in ordinal order. */
    static List<String> shimFiles(Path shimDir) throws IOException {
        Path pom = shimDir.resolve("pom.xml");
        Path main = shimDir.resolve("src").resolve("main");
        if (!Files.isRegularFile(pom) || !Files.isDirectory(main)) {
            throw new IllegalArgumentException(shimDir + " needs pom.xml and src/main");
        }
        List<String> files = new ArrayList<>();
        files.add("pom.xml");
        try (Stream<Path> walk = Files.walk(main)) {
            walk.filter(Files::isRegularFile)
                    .map(f -> shimDir.relativize(f).toString().replace('\\', '/'))
                    .forEach(files::add);
        }
        files.sort(Comparator.naturalOrder());
        return files;
    }

    private static byte[] lf(byte[] content) {
        ByteArrayOutputStream out = new ByteArrayOutputStream(content.length);
        for (int i = 0; i < content.length; i++) {
            if (content[i] == '\r' && i + 1 < content.length && content[i + 1] == '\n') {
                continue;
            }
            out.write(content[i]);
        }
        return out.toByteArray();
    }
}
