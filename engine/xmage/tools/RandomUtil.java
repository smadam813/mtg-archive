package mage.util;

import java.awt.*;
import java.util.Collection;
import java.util.Random;
import java.util.UUID;

/**
 * Created by IGOUDT on 5-9-2016.
 * <p>
 * Spike patch: one random generator per thread, and a seeded object-ID stream.
 * setSeed seeds both. newUUID falls back to UUID.randomUUID() until a seed is set.
 */
public final class RandomUtil {

    private static final ThreadLocal<Random> random = ThreadLocal.withInitial(Random::new);
    private static final ThreadLocal<Random> idRandom = new ThreadLocal<>();

    private RandomUtil() {
    }

    public static Random getRandom() {
        return random.get();
    }

    public static int nextInt() {
        return random.get().nextInt();
    }

    public static int nextInt(int max) {
        return random.get().nextInt(max);
    }

    public static boolean nextBoolean() {
        return random.get().nextBoolean();
    }

    public static double nextDouble() {
        return random.get().nextDouble();
    }

    public static Color nextColor() {
        return new Color(RandomUtil.nextInt(256), RandomUtil.nextInt(256), RandomUtil.nextInt(256));
    }

    public static void setSeed(long newSeed) {
        random.get().setSeed(newSeed);
        idRandom.set(new Random(newSeed * 0x9E3779B97F4A7C15L + 0x632BE59BD9B4E019L));
    }

    public static UUID newUUID() {
        Random r = idRandom.get();
        if (r == null) {
            return UUID.randomUUID();
        }
        long hi = (r.nextLong() & 0xFFFFFFFFFFFF0FFFL) | 0x0000000000004000L;
        long lo = (r.nextLong() & 0x3FFFFFFFFFFFFFFFL) | 0x8000000000000000L;
        return new UUID(hi, lo);
    }

    public static <T> T randomFromCollection(Collection<T> collection) {
        if (collection.size() < 2) {
            return collection.stream().findFirst().orElse(null);
        }
        int rand = nextInt(collection.size());
        int count = 0;
        // Callers often pass a HashSet of game objects, which iterates in JVM identity-hash order.
        // Sort by a stable key first, so the same seed picks the same object.
        for (T current : stableOrder(collection)) {
            if (count == rand) {
                return current;
            }
            count++;
        }
        return null;
    }

    private static <T> Collection<T> stableOrder(Collection<T> collection) {
        if (collection instanceof java.util.List || collection instanceof java.util.LinkedHashSet
                || collection instanceof java.util.SortedSet) {
            return collection;
        }
        java.util.List<T> list = new java.util.ArrayList<>(collection);
        Object first = list.get(0);
        if (first instanceof mage.MageItem) {
            list.sort(java.util.Comparator.comparing(o -> ((mage.MageItem) o).getId()));
        } else if (first instanceof Comparable) {
            list.sort(java.util.Comparator.comparing(o -> String.valueOf(o)));
        }
        return list;
    }
}
