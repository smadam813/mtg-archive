package mtgarchive.xmage;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.google.gson.JsonObject;
import com.networknt.schema.JsonSchema;
import com.networknt.schema.JsonSchemaFactory;
import com.networknt.schema.SpecVersion;
import com.networknt.schema.ValidationMessage;

import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.util.Set;

/**
 * Test helpers for the phase-0 contract (engine/xmage/protocol, on the test classpath through
 * pom.xml): the wire schema, and the record each sidecar message maps onto.
 */
final class WireContract {

    static final ObjectMapper JSON = new ObjectMapper();

    private WireContract() {
    }

    /** pilot-wire.schema.json as a tree. */
    static JsonNode schemaTree() {
        try (InputStream in = WireContract.class.getResourceAsStream("/pilot-wire.schema.json")) {
            return JSON.readTree(in);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    /** The schema root: any message of the protocol. */
    static JsonSchema rootSchema() {
        return JsonSchemaFactory.getInstance(SpecVersion.VersionFlag.V202012).getSchema(schemaTree());
    }

    /** One definition of the schema, such as "action", with every other definition in reach. */
    static JsonSchema definition(String name) {
        JsonNode root = schemaTree();
        ObjectNode schema = JSON.createObjectNode();
        schema.put("$schema", root.get("$schema").asText());
        schema.set("$defs", root.get("$defs"));
        schema.put("$ref", "#/$defs/" + name);
        return JsonSchemaFactory.getInstance(SpecVersion.VersionFlag.V202012).getSchema(schema);
    }

    /** The schema errors of one JSON text. */
    static Set<ValidationMessage> validate(JsonSchema schema, String json) {
        try {
            return schema.validate(JSON.readTree(json));
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    /** The record a sidecar-to-service message maps onto, or null for a service message. */
    static Class<?> sidecarRecord(JsonObject message) {
        String type = Wire.typeOf(message);
        String kind = message.has("kind") ? message.get("kind").getAsString() : "";
        if ("hello".equals(type) && message.has("engine")) {
            return Messages.HelloReply.class;
        }
        if ("deck".equals(type)) {
            return Messages.DeckReply.class;
        }
        if ("game_over".equals(type)) {
            return Messages.GameOver.class;
        }
        if ("scenario_result".equals(type)) {
            return Messages.ScenarioResult.class;
        }
        if ("decide".equals(type)) {
            switch (kind) {
                case "keep": return Messages.DecideKeep.class;
                case "bottom": case "discard": return Messages.DecideCount.class;
                case "pick": return Messages.DecidePick.class;
                case "respond": return Messages.DecideRespond.class;
                default: return Messages.DecideAction.class;
            }
        }
        if ("event".equals(type) && kind.equals("end_of_turn")) {
            return Messages.EventEndOfTurn.class;
        }
        if ("event".equals(type) && kind.equals("cast")) {
            return Messages.EventCast.class;
        }
        if ("event".equals(type) && kind.equals("resolved")) {
            return Messages.EventResolved.class;
        }
        if ("event".equals(type) && kind.equals("opposed")) {
            return message.has("reason") ? Messages.EventNotCast.class : Messages.EventOpposed.class;
        }
        return null;
    }

    /** Parses a decide or an event into its record. */
    static Messages.InGame inGame(JsonObject message) {
        Class<?> type = sidecarRecord(message);
        if (type == null || !Messages.InGame.class.isAssignableFrom(type)) {
            throw new IllegalArgumentException("not a decide or an event of phase 1B: " + message);
        }
        return (Messages.InGame) Wire.GSON.fromJson(message, type);
    }
}
