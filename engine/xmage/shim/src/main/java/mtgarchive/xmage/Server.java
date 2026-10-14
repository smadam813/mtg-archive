package mtgarchive.xmage;

import mage.util.ThreadUtils;

import java.io.IOException;
import java.net.ServerSocket;
import java.net.Socket;
import java.util.concurrent.Semaphore;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * The TCP server: one thread per connection. At most {@link ShimConfig#threads()} games play at
 * once; a start_game beyond that waits for a free game slot.
 */
public final class Server implements AutoCloseable {

    private final ServerSocket listener;
    private final ShimConfig config;
    private final DeckLibrary decks;
    private final Semaphore games;
    private final String version;
    private final AtomicInteger connections = new AtomicInteger();

    public Server(ShimConfig config, DeckLibrary decks, String version) throws IOException {
        this.listener = new ServerSocket(config.port());
        this.config = config;
        this.decks = decks;
        this.games = new Semaphore(config.threads(), true);
        this.version = version;
    }

    public int port() {
        return listener.getLocalPort();
    }

    /**
     * Accepts connections until {@link #close()}. Each connection plays its games on its own thread,
     * so that thread gets the GAME name prefix and the 64 MB stack that XMage game code needs.
     */
    public void serve() {
        while (!listener.isClosed()) {
            try {
                Socket socket = listener.accept();
                socket.setTcpNoDelay(true);
                Thread thread = new Thread(null, new Connection(socket, config, decks, games, version),
                        ThreadUtils.THREAD_PREFIX_GAME + "-conn-" + connections.incrementAndGet(), GameRunner.GAME_THREAD_STACK);
                thread.setDaemon(true);
                thread.start();
            } catch (IOException e) {
                if (!listener.isClosed()) {
                    System.err.println("XMAGE accept failed: " + e);
                }
            }
        }
    }

    /** Serves on a daemon thread; for tests. */
    public Server startInBackground() {
        Thread thread = new Thread(this::serve, "accept");
        thread.setDaemon(true);
        thread.start();
        return this;
    }

    @Override
    public void close() throws IOException {
        listener.close();
    }
}
