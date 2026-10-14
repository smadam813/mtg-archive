package mtgarchive.xmage;

import mage.cards.repository.CardCriteria;
import mage.cards.repository.CardRepository;
import mage.cards.repository.CardScanner;
import mage.cards.repository.RepositoryUtil;

import java.util.ArrayList;
import java.util.List;

/**
 * Builds the XMage card database (./db/cards.h2) in the current directory.
 * Runs once at image build time, so the sidecar never scans card classes.
 */
public final class DbInit {
    public static void main(String[] args) {
        long t0 = System.nanoTime();
        List<String> errors = new ArrayList<>();
        RepositoryUtil.bootstrapLocalDb();
        CardScanner.scan(errors);
        long t1 = System.nanoTime();
        int cards = CardRepository.instance.findCards(new CardCriteria()).size();
        CardRepository.instance.closeDB(true);
        System.out.println("RESULT dbinit_ms=" + (t1 - t0) / 1_000_000 + " cards=" + cards + " scan_errors=" + errors.size());
        for (String e : errors) {
            System.out.println("SCAN_ERROR " + e);
        }
        System.exit(0);
    }
}
