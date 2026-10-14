package mtgarchive.xmage;

import mage.cards.repository.TokenRepository;
import mage.cards.repository.TokenType;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Enumeration;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;

/** One-time JVM set-up that seeded games need (taken from the spike's Main.run). */
public final class Engine {

    private static boolean warm;

    private Engine() {
    }

    /**
     * Opens the card database, loads the idle deck, preloads the token repository and initializes
     * every class of the XMage core jar. Safe to call more than once.
     */
    public static synchronized void warmUp() {
        if (warm) {
            return;
        }
        DeckLibrary.idle();
        // The token repository fills its caches lazily and is not thread-safe on first use.
        // XMage's server preloads it at startup (RepositoryUtil.bootstrapLocalDb); do the same.
        TokenRepository.instance.getAll();
        for (TokenType type : TokenType.values()) {
            TokenRepository.instance.getByType(type);
        }
        preinitEngineClasses();
        warm = true;
    }

    /**
     * Initialize every class of the engine jar before the first seeded game.
     * Some engine classes hold static game objects (keyword ability singletons, StackAbility's empty
     * cost). Their constructors draw object IDs. Without this, the class initializes inside whichever
     * game first touches it and shifts that game's seeded ID stream.
     * Skips mage.cards.Sets: its initializer loads every set and card class.
     * Logs one WARN line with the number of classes that failed to initialize and the first names.
     */
    static int preinitEngineClasses() {
        int n = 0;
        List<String> failed = new ArrayList<>();
        ClassLoader cl = Engine.class.getClassLoader();
        String jar = mage.MageObject.class.getProtectionDomain().getCodeSource().getLocation().getPath();
        try (JarFile jf = new JarFile(jar)) {
            Enumeration<JarEntry> en = jf.entries();
            while (en.hasMoreElements()) {
                String name = en.nextElement().getName();
                if (!name.endsWith(".class") || name.startsWith("META-INF") || name.contains("module-info")
                        || name.equals("mage/cards/Sets.class")) {
                    continue;
                }
                String className = name.substring(0, name.length() - 6).replace('/', '.');
                try {
                    Class.forName(className, true, cl);
                    n++;
                } catch (Throwable e) {
                    // classes whose initializer needs a running server or a missing optional dependency
                    failed.add(className);
                }
            }
        } catch (IOException e) {
            throw new IllegalStateException(e);
        }
        if (!failed.isEmpty()) {
            System.err.println("XMAGE WARN warm-up could not initialize " + failed.size() + " engine classes, first: "
                    + String.join(", ", failed.subList(0, Math.min(5, failed.size()))));
        }
        return n;
    }
}
