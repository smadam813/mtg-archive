using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgArchive.Simulation.Xmage;

/// <summary>
/// Wire protocol version 2 between the service and the XMage sidecar
/// (<c>engine/xmage/protocol/pilot-wire.schema.json</c>). Version 2 adds
/// the scripted opponent to <c>start_game</c>, the <c>oppose</c> reply,
/// the <c>opposed</c> event and the <c>respond</c> decide (ADR 0013). Each message is
/// a 4-byte big-endian length, then that many bytes of UTF-8 JSON with
/// snake_case field names. Dictionary
/// keys stay as sent, because the keys of <c>finds</c> are card names.
/// </summary>
public static class XmageWire
{
    public const int Protocol = 2;

    /// <summary>The largest frame the service reads. A view of a whole deck is far below it.</summary>
    public const int MaxFrameBytes = 16 * 1024 * 1024;

    /// <summary>Reads the sidecar's messages into the records below.</summary>
    public static JsonSerializerOptions Json { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static async Task WriteFrameAsync(Stream stream, JsonNode message, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One frame's JSON, or null when the stream ends before a
    /// frame starts. A stream that ends inside a frame throws
    /// <see cref="EndOfStreamException"/>; a length outside 0 to
    /// <see cref="MaxFrameBytes"/> throws <see cref="InvalidDataException"/>.</summary>
    public static async Task<JsonDocument?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await FillAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 0 or > MaxFrameBytes)
        {
            throw new InvalidDataException($"A frame of {length} bytes is outside 0 to {MaxFrameBytes}.");
        }

        var body = new byte[length];
        if (!await FillAsync(stream, body, cancellationToken).ConfigureAwait(false))
        {
            throw new EndOfStreamException("The stream ended inside a frame.");
        }

        return JsonDocument.Parse(body);
    }

    /// <summary>Fills the buffer. False when the stream ends before the
    /// first byte; an end after it throws.</summary>
    private static async Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                return read == 0 && buffer.Length > 0
                    ? false
                    : throw new EndOfStreamException("The stream ended inside a frame.");
            }

            read += n;
        }

        return true;
    }
}

/// <summary>
/// The messages the service sends: the requests outside a game, and the
/// replies inside one. A reply carries its <c>seq</c> and its kind's field
/// only. A pass writes <c>"action": null</c>.
/// </summary>
public static class XmageRequests
{
    public static JsonObject Hello() => new() { ["type"] = "hello", ["protocol"] = XmageWire.Protocol };

    /// <summary>The deck by full decklist name: the commanders, then every other card with its quantity.</summary>
    public static JsonObject LoadDeck(IReadOnlyList<DeckCard> deck) => new()
    {
        ["type"] = "load_deck",
        ["commanders"] = Array(deck.Where(c => c.IsCommander).Select(c => c.Name)),
        ["cards"] = new JsonArray([.. deck.Where(c => !c.IsCommander).Select(c => (JsonNode?)new JsonObject { ["name"] = c.Name, ["quantity"] = c.Quantity })]),
    };

    /// <summary>One game: the deck, the seed, the horizon, the game rules
    /// it plays under, and the opponent. A null <paramref name="opponent"/>
    /// seats the idle opponent; a list seats the scripted opponent with
    /// those disruption cards (ADR 0013).</summary>
    public static JsonObject StartGame(string deckId, int seed, int horizon, GameRules rules, bool watchCasts, bool log, IReadOnlyList<string>? opponent = null) => new()
    {
        ["type"] = "start_game",
        ["deck_id"] = deckId,
        ["seed"] = seed,
        ["horizon"] = horizon,
        ["start_life"] = rules.StartLife,
        ["commander_damage"] = rules.CommanderDamage,
        ["starting_player_draws"] = rules.StartingPlayerDraws,
        ["free_mulligans"] = rules.FreeMulligans,
        ["watch_casts"] = watchCasts,
        ["log"] = log,
        ["opponent"] = opponent is null ? null : new JsonObject { ["cards"] = Array(opponent) },
    };

    /// <summary>An action decision's reply: a listed action id, or null to pass.</summary>
    public static JsonObject Action(int seq, string? action) => new() { ["seq"] = seq, ["action"] = action };

    public static JsonObject Keep(int seq, bool keep) => new() { ["seq"] = seq, ["keep"] = keep };

    /// <summary>A bottom or discard reply: card names from the hand.</summary>
    public static JsonObject Cards(int seq, IEnumerable<string> cards) => new() { ["seq"] = seq, ["cards"] = Array(cards) };

    public static JsonObject Option(int seq, string option) => new() { ["seq"] = seq, ["option"] = option };

    /// <summary>An event's reply: <c>continue</c>, <c>stop</c> or <c>counter</c>.</summary>
    public static JsonObject Do(int seq, string what) => new() { ["seq"] = seq, ["do"] = what };

    /// <summary>An end-of-turn reply that removes permanents (tier 3): each id is a permanent of the view.</summary>
    public static JsonObject Disrupt(int seq, IEnumerable<string> remove) => new() { ["seq"] = seq, ["do"] = "disrupt", ["remove"] = Array(remove) };

    /// <summary>An interactive reply (ADR 0013): the scripted opponent casts
    /// <paramref name="card"/>, a disruption card of <c>start_game</c>, at
    /// the first of <paramref name="targets"/> it can target and affect.</summary>
    public static JsonObject Oppose(int seq, string card, IEnumerable<string> targets)
        => new() { ["seq"] = seq, ["do"] = "oppose", ["card"] = card, ["targets"] = Array(targets) };

    private static JsonArray Array(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode?)JsonValue.Create(v))]);
}

/// <summary>One message from the sidecar: its <c>type</c>, null for a
/// reply-shaped message, and its JSON.</summary>
public sealed record XmageMessage(string? Type, JsonElement Json)
{
    public static XmageMessage Of(JsonDocument document)
    {
        var root = document.RootElement.Clone();
        var type = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;
        return new XmageMessage(type, root);
    }

    public T As<T>() => Json.Deserialize<T>(XmageWire.Json) ?? throw new JsonException($"A {Type} message is null.");
}

/// <summary>The hello reply: the engine, its version, its game threads, the protocol.</summary>
public sealed record WireHello(string Type, string Engine, string Version, int Threads, int Protocol);

/// <summary>The load_deck reply: the deck id and the cards XMage lacks.</summary>
public sealed record WireDeck(string Type, string DeckId, IReadOnlyList<string> Unimplemented);

/// <summary>A decision the service answers once. <see cref="Keep"/>,
/// <see cref="Count"/>, <see cref="Pick"/> and <see cref="Respond"/> are
/// set only on their kind.</summary>
public sealed record WireDecide(string Type, int Seq, string Kind, WireView View, WireKeep? Keep, int? Count, WirePick? Pick, WireRespond? Respond = null);

/// <summary>The response window (ADR 0013): the disruption card on top of
/// the stack, and the cards it aims at. The view lists our legal actions.</summary>
public sealed record WireRespond(WireThreat Threat, IReadOnlyList<WireRespondVictim> Victims);

/// <summary>The disruption card: "s:" and its stack id, and its name.</summary>
public sealed record WireThreat(string Id, string Card);

/// <summary>A card the disruption card aims at: a spell ("s:") or a
/// permanent ("p:"), its decklist name and face.</summary>
public sealed record WireRespondVictim(string Id, string Card, string Face);

public sealed record WireKeep(IReadOnlyList<string> Hand, int MulligansTaken, int KeepSize);

/// <summary>A pick. <see cref="Seen"/> is null for a search; for a scry or
/// a look it lists the looked-at cards, top first.</summary>
public sealed record WirePick(string Purpose, IReadOnlyList<WirePickOption> Options, IReadOnlyList<string>? Seen);

/// <summary>One pick option; the stop option has id "none" and no card.</summary>
public sealed record WirePickOption(string Id, string Label, string? Card);

/// <summary>An event the service answers once: <c>end_of_turn</c>,
/// <c>resolved</c>, <c>cast</c> or <c>opposed</c>. <see cref="Card"/>,
/// <see cref="Outcome"/>, <see cref="Victims"/> and <see cref="Lost"/> are
/// set on <c>opposed</c> only, and <see cref="Reason"/> only when the
/// outcome is <c>not_cast</c>.</summary>
public sealed record WireEvent(
    string Type,
    int Seq,
    string Kind,
    WireView View,
    bool? AfterDisruption,
    WireSpell? Spell,
    string? Card = null,
    string? Outcome = null,
    string? Reason = null,
    IReadOnlyList<WireVictim>? Victims = null,
    IReadOnlyList<string>? Lost = null);

/// <summary>A card a disruption card aimed at: a spell ("s:") or a
/// permanent ("p:"), its decklist name and face, and whether it was hit.</summary>
public sealed record WireVictim(string Id, string Card, string Face, bool Hit);

/// <summary>Our spell on the stack: "s:" and its id, its decklist name, and the face it was cast as.</summary>
public sealed record WireSpell(string Id, string Card, string Face);

/// <summary>The end of a game. <see cref="Reason"/> is horizon, stopped,
/// life, win or engine_error; <see cref="Error"/> is set only on
/// engine_error. <see cref="View"/> is the final view, in the event form,
/// on a win or a life ending, and null on every other reason.</summary>
public sealed record WireGameOver(string Type, int Turns, string Reason, string Digest, string? Error, IReadOnlyList<WireLogEntry>? Log, WireView? View);

public sealed record WireLogEntry(int Turn, string Step, string Message);

/// <summary>The pilot view on the wire (roadmap, "The view").</summary>
public sealed record WireView(
    int Turn,
    string Step,
    int Life,
    bool LandDropUsed,
    string Pool,
    IReadOnlyList<string> Hand,
    IReadOnlyList<string> Graveyard,
    IReadOnlyList<string> Exile,
    IReadOnlyList<string> CommandZone,
    IReadOnlyList<WirePermanent> Battlefield,
    int LibraryCount,
    IReadOnlyList<WireManaSource> ManaSources,
    IReadOnlyList<WireHandMana> HandMana,
    IReadOnlyList<WireResolved> Resolved,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Finds,
    IReadOnlyList<WireAction> Actions);

public sealed record WirePermanent(string Id, string Card, string Face, bool Tapped);

public sealed record WireManaSource(string Id, IReadOnlyList<string> Options);

public sealed record WireHandMana(string Card, IReadOnlyList<string> Options);

public sealed record WireResolved(string Card, string Face, int Turn);

public sealed record WireAction(string Id, string Kind, string Card, string Face, string Zone, string Cost, string? PermanentId, bool SpendsSource);
