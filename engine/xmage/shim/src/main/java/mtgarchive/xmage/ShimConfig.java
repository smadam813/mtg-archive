package mtgarchive.xmage;

import java.nio.file.Path;
import java.util.Map;

/**
 * Sidecar settings from the environment.
 *
 * @param port              XMAGE_PORT, default 7070
 * @param threads           XMAGE_THREADS, default 24: the most games that play at once
 * @param decisionTimeoutMs XMAGE_DECISION_TIMEOUT_MS, default 5000: the wait for one reply
 * @param home              XMAGE_HOME, default /opt/xmage: holds XMAGE_TAG, patches/ and shim/
 */
public record ShimConfig(int port, int threads, int decisionTimeoutMs, Path home) {

    public static ShimConfig fromEnv(Map<String, String> env) {
        return new ShimConfig(
                intOf(env, "XMAGE_PORT", 7070),
                intOf(env, "XMAGE_THREADS", 24),
                intOf(env, "XMAGE_DECISION_TIMEOUT_MS", 5000),
                Path.of(env.getOrDefault("XMAGE_HOME", "/opt/xmage")));
    }

    private static int intOf(Map<String, String> env, String key, int fallback) {
        String value = env.get(key);
        if (value == null || value.isBlank()) {
            return fallback;
        }
        int parsed = Integer.parseInt(value.trim());
        if (parsed < 1 && !key.equals("XMAGE_PORT")) {
            throw new IllegalArgumentException(key + " must be at least 1, got " + parsed);
        }
        return parsed;
    }
}
