package mtgarchive.xmage;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.networknt.schema.JsonSchema;
import com.networknt.schema.ValidationMessage;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The phase-0 contract, engine/xmage/protocol (on the test classpath through pom.xml): every
 * message the sidecar sends validates against pilot-wire.schema.json, and the examples the
 * sidecar would send survive a round trip through the wire records unchanged.
 */
class SchemaConformanceTest {

    private static JsonSchema schema;

    @BeforeAll
    static void load() {
        schema = WireContract.rootSchema();
    }

    private static void assertValid(String json) {
        Set<ValidationMessage> errors = WireContract.validate(schema, json);
        assertTrue(errors.isEmpty(), errors + " in " + json);
    }

    private static void assertInvalid(String json) {
        Set<ValidationMessage> errors = WireContract.validate(schema, json);
        assertFalse(errors.isEmpty(), "expected a schema error in " + json);
    }

    private static List<Path> examplesIn(String subfolder) throws Exception {
        URL dir = SchemaConformanceTest.class.getResource("/examples/" + subfolder);
        try (Stream<Path> files = Files.list(Path.of(dir.toURI()))) {
            return files.filter(f -> f.toString().endsWith(".json")).sorted().toList();
        }
    }

    private static List<Path> validExamples() throws Exception {
        return examplesIn("valid");
    }

    private static List<Path> invalidExamples() throws Exception {
        return examplesIn("invalid");
    }

    @Test
    void everyValidExampleValidatesAgainstTheSchemaRoot() throws Exception {
        List<Path> examples = validExamples();
        assertFalse(examples.isEmpty(), "phase 0 ships engine/xmage/protocol/examples/valid/*.json");
        for (Path example : examples) {
            assertValid(Files.readString(example, StandardCharsets.UTF_8));
        }
    }

    @Test
    void everyInvalidExampleFailsTheSchemaRoot() throws Exception {
        List<Path> examples = invalidExamples();
        assertFalse(examples.isEmpty(), "phase 0 ships engine/xmage/protocol/examples/invalid/*.json");
        for (Path example : examples) {
            assertInvalid(Files.readString(example, StandardCharsets.UTF_8));
        }
    }

    @Test
    void sidecarExamplesRoundTripThroughTheWireRecords() throws Exception {
        int checked = 0;
        for (Path example : validExamples()) {
            JsonObject message = JsonParser.parseString(Files.readString(example, StandardCharsets.UTF_8)).getAsJsonObject();
            Class<?> type = WireContract.sidecarRecord(message);
            if (type == null) {
                continue;
            }
            JsonElement again = JsonParser.parseString(Wire.GSON.toJson(Wire.GSON.fromJson(message, type)));
            assertEquals(message, again, example.getFileName().toString());
            checked++;
        }
        assertTrue(checked > 0, "no example of a message the sidecar sends");
    }

    /** The start_game records the service's examples describe read into the Java record unchanged. */
    @Test
    void theStartGameExamplesRoundTripThroughTheRecord() throws Exception {
        for (String name : List.of("start_game", "start_game_interactive")) {
            JsonObject message = JsonParser.parseString(Files.readString(
                    Path.of(SchemaConformanceTest.class.getResource("/examples/valid/" + name + ".json").toURI()),
                    StandardCharsets.UTF_8)).getAsJsonObject();
            Messages.StartGame start = Wire.GSON.fromJson(message, Messages.StartGame.class);
            assertEquals(message, JsonParser.parseString(Wire.GSON.toJson(start)), name);
            assertValid(Wire.GSON.toJson(start));
        }
        assertInvalid(Wire.GSON.toJson(new Messages.StartGame("start_game", TestGames.obeka().deckId(), 1, 10, 40, true, true, 1,
                false, false, new Messages.Opponent(List.of()))));
    }

    @Test
    void everyMessageTheSidecarSendsValidates() throws Exception {
        List<String> sent = new ArrayList<>();
        sent.add(Wire.GSON.toJson(new Messages.HelloReply("hello", "xmage", "xmage-1.4.62V1+0123abcd", 24, Messages.PROTOCOL)));
        LoadedDeck deck = TestGames.obeka();
        sent.add(Wire.GSON.toJson(new Messages.DeckReply("deck", deck.deckId(), List.of("Some 2026 Card"))));
        sent.add(Wire.GSON.toJson(GameCtx.errorOver("unknown deck_id nope; send load_deck first")));
        for (long seed = 1; seed <= 4; seed++) {
            TestGames.Recorder r = new TestGames.Recorder(TestGames.first());
            Messages.GameOver over = TestGames.play(
                    new Messages.StartGame("start_game", deck.deckId(), seed, 10, 40, true, true, 1, false, true), deck, r);
            r.messages.forEach(m -> sent.add(Wire.GSON.toJson(m)));
            sent.add(Wire.GSON.toJson(over));
        }
        sent.add(Wire.GSON.toJson(TestGames.play(5, 10, m -> m instanceof Messages.EventEndOfTurn
                ? Messages.Reply.ofDo(m.seq(), "stop") : TestGames.pass().ask(m))));

        // protocol 2: a game against the scripted opponent sends the same messages
        TestGames.Recorder scripted = new TestGames.Recorder(TestGames.first());
        sent.add(Wire.GSON.toJson(TestGames.play(TestGames.start(3, 3).withOpponent(ScriptedOpponentTest.TABLE), deck, scripted)));
        scripted.messages.forEach(m -> sent.add(Wire.GSON.toJson(m)));

        // a combo win: its game_over carries the final view
        LoadedDeck oracle = TestGames.oracle();
        Messages.GameOver win = TestGames.play(
                new Messages.StartGame("start_game", oracle.deckId(), 7, 5, 40, true, true, 1, false, true), oracle, TestGames.castOracle());
        assertEquals("win", win.reason(), win.error());
        sent.add(Wire.GSON.toJson(win));

        // two rejected hands force a keep of six plus the London draw, and hold seven: this bottoms, never discards
        int[] keeps = {0};
        TestGames.Recorder mulligans = new TestGames.Recorder(m -> m instanceof Messages.DecideKeep
                ? Messages.Reply.ofKeep(m.seq(), ++keeps[0] > 2) : TestGames.pass().ask(m));
        TestGames.play(11, 1, mulligans);
        mulligans.messages.forEach(m -> sent.add(Wire.GSON.toJson(m)));

        // a passing pilot draws to eight on turn 1 (the play, no land played) and discards one at cleanup
        TestGames.Recorder discards = new TestGames.Recorder(TestGames.pass());
        TestGames.play(7, 1, discards);
        assertTrue(discards.all(Messages.DecideCount.class).stream().anyMatch(d -> "discard".equals(d.kind())),
                "no discard decision was seen");
        discards.messages.forEach(m -> sent.add(Wire.GSON.toJson(m)));

        // tier 3: a game whose cast events are answered counter, then continue
        LoadedDeck needles = DisruptionTest.needles();
        TestGames.Recorder counters = new TestGames.Recorder(DisruptionTest.caster("Pithing Needle", DisruptionTest.counterFirstCast()));
        sent.add(Wire.GSON.toJson(TestGames.play(DisruptionTest.start(needles, 7, 3, true), needles, counters)));
        counters.messages.forEach(m -> sent.add(Wire.GSON.toJson(m)));

        // tier 3: a disruption at the end of the turn, then the after view
        TestGames.Recorder removals = new TestGames.Recorder(DisruptionTest.caster("Pithing Needle", DisruptionTest.removeFirst("Pithing Needle")));
        sent.add(Wire.GSON.toJson(TestGames.play(DisruptionTest.start(needles, 7, 3, false), needles, removals)));
        removals.messages.forEach(m -> sent.add(Wire.GSON.toJson(m)));
        assertTrue(sent.stream().anyMatch(s -> s.contains("\"after_disruption\":true")), "no after view was checked");

        // interactive: the opponent's Counterspell hits, and on a Stag it is not cast
        TestGames.Recorder opposed = new TestGames.Recorder(InteractiveDisruptionTest.counterEveryCast("Pithing Needle"));
        sent.add(Wire.GSON.toJson(TestGames.play(InteractiveDisruptionTest.start(needles, 7, 3), needles, opposed)));
        opposed.messages.forEach(m -> sent.add(Wire.GSON.toJson(m)));
        LoadedDeck stags = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Forest", 60),
                new Messages.DeckEntry("Great Sable Stag", 39));
        TestGames.Recorder notCast = new TestGames.Recorder(InteractiveDisruptionTest.counterEveryCast("Great Sable Stag"));
        sent.add(Wire.GSON.toJson(TestGames.play(InteractiveDisruptionTest.start(stags, 7, 4), stags, notCast)));
        notCast.messages.forEach(m -> sent.add(Wire.GSON.toJson(m)));
        assertTrue(sent.stream().anyMatch(s -> s.contains("\"outcome\":\"hit\"")), "no hit was checked");
        assertTrue(sent.stream().anyMatch(s -> s.contains("\"reason\":\"no_target\"")), "no not_cast was checked");

        // interactive: the response window, answered with our Counterspell
        LoadedDeck answers = ResponseWindowTest.deckWith(InteractiveDisruptionTest.COUNTERSPELL);
        TestGames.Recorder responded = new TestGames.Recorder(
                ResponseWindowTest.opposeWhenHeld(InteractiveDisruptionTest.COUNTERSPELL, InteractiveDisruptionTest.COUNTERSPELL));
        sent.add(Wire.GSON.toJson(TestGames.play(InteractiveDisruptionTest.start(answers, 11, 6), answers, responded)));
        responded.messages.forEach(m -> sent.add(Wire.GSON.toJson(m)));
        assertTrue(sent.stream().anyMatch(s -> s.contains("\"outcome\":\"answered\"")), "no answered was checked");

        // a real loss: game_over with reason life and its final view
        LoadedDeck eight = TestGames.eightIslands();
        Messages.GameOver loss = TestGames.play(new Messages.StartGame("start_game", eight.deckId(), 7, 5, 40, true, true, 1, false, true),
                eight, TestGames.pass());
        assertEquals("life", loss.reason(), loss.error());
        sent.add(Wire.GSON.toJson(loss));

        // a bad event reply ends the game with engine_error, and that game_over validates too
        Messages.GameOver bad = TestGames.play(5, 10, m -> m instanceof Messages.EventEndOfTurn
                ? Messages.Reply.ofDo(m.seq(), "counter") : TestGames.pass().ask(m));
        assertEquals("engine_error", bad.reason());
        sent.add(Wire.GSON.toJson(bad));

        // scenario mode: a result and an error
        sent.add(Wire.GSON.toJson(TestGames.scenario(ScenarioTest.scenario(List.of("Dark Ritual"), List.of(ScenarioTest.untapped("Swamp")),
                List.of(), List.of(), List.of(ScenarioTest.cast("Dark Ritual"))))));
        sent.add(Wire.GSON.toJson(TestGames.scenario(ScenarioTest.scenario(List.of("Dark Ritual"), List.of(),
                List.of(), List.of(), List.of(ScenarioTest.cast("Dark Ritual"))))));

        for (String kind : List.of("\"keep\"", "\"bottom\"", "\"discard\"", "\"pick\"", "\"action\"", "\"end_of_turn\"", "\"resolved\"", "\"cast\"", "\"opposed\"", "\"respond\"")) {
            assertTrue(sent.stream().anyMatch(s -> s.contains("\"kind\":" + kind)), "no " + kind + " message was checked");
        }
        for (String json : sent) {
            assertValid(json);
        }
    }
}
