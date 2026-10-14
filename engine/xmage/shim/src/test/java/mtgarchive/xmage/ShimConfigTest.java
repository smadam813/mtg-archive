package mtgarchive.xmage;

import org.junit.jupiter.api.Test;

import java.nio.file.Path;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

class ShimConfigTest {

    @Test
    void defaultsMatchTheComposeService() {
        assertEquals(new ShimConfig(7070, 24, 5000, Path.of("/opt/xmage")), ShimConfig.fromEnv(Map.of()));
    }

    @Test
    void theEnvironmentOverridesEachSetting() {
        assertEquals(new ShimConfig(7171, 8, 250, Path.of("/tmp/x")), ShimConfig.fromEnv(Map.of(
                "XMAGE_PORT", "7171", "XMAGE_THREADS", "8", "XMAGE_DECISION_TIMEOUT_MS", "250", "XMAGE_HOME", "/tmp/x")));
    }

    @Test
    void zeroThreadsIsRefused() {
        assertThrows(IllegalArgumentException.class, () -> ShimConfig.fromEnv(Map.of("XMAGE_THREADS", "0")));
    }
}
