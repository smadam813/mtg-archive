package mtgarchive.xmage;

import com.google.gson.FieldNamingPolicy;
import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.nio.charset.StandardCharsets;

/** JSON for the wire: snake_case names, nulls written out, no HTML escaping. */
public final class Wire {

    public static final Gson GSON = new GsonBuilder()
            .setFieldNamingPolicy(FieldNamingPolicy.LOWER_CASE_WITH_UNDERSCORES)
            .serializeNulls()
            .disableHtmlEscaping()
            .create();

    private Wire() {
    }

    /** Parses one frame body. Throws JsonParseException or IllegalStateException on a frame that is not a JSON object. */
    public static JsonObject parse(byte[] frame) {
        return JsonParser.parseString(new String(frame, StandardCharsets.UTF_8)).getAsJsonObject();
    }

    /** The message's type, or null for a reply (replies carry a seq and no type). */
    public static String typeOf(JsonObject message) {
        JsonElement type = message.get("type");
        return type != null && type.isJsonPrimitive() ? type.getAsString() : null;
    }
}
