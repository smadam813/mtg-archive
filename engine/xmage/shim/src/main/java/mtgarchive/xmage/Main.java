package mtgarchive.xmage;

/** The sidecar: warm the engine up, then listen. The port opens only when the engine is ready. */
public final class Main {

    private Main() {
    }

    public static void main(String[] args) throws Exception {
        ShimConfig config = ShimConfig.fromEnv(System.getenv());
        String version = Version.of(config.home().resolve("XMAGE_TAG"), config.home().resolve("patches"),
                config.home().resolve("shim"));
        Engine.warmUp();
        try (Server server = new Server(config, new DeckLibrary(), version)) {
            System.out.println("XMAGE listening port=" + server.port() + " threads=" + config.threads()
                    + " decision_timeout_ms=" + config.decisionTimeoutMs() + " version=" + version);
            server.serve();
        }
    }
}
