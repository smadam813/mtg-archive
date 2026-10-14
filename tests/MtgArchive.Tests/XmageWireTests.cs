using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// The service's side of wire protocol 2 (spec §4.2, ADR 0013): every message the
/// service sends validates against the schema that the Java tests also
/// use, frames are a big-endian length and UTF-8 JSON, and every message
/// the sidecar sends reads into its record.
/// </summary>
public sealed class XmageWireTests
{
    private static readonly string ProtocolRoot = Path.Combine(RepoRoot.Find(), "engine", "xmage", "protocol");

    private static readonly JsonSchema Schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(ProtocolRoot, "pilot-wire.schema.json")));

    private static readonly IReadOnlyList<DeckCard> Deck =
    [
        TestDeckCards.Commander("Obeka, Brute Chronologist", "{1}{U}{B}{R}"),
        TestDeckCards.Land("Island", "U", quantity: 60),
        TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact"),
    ];

    public static TheoryData<string, string> Sent() => new()
    {
        { "hello", XmageRequests.Hello().ToJsonString() },
        { "load_deck", XmageRequests.LoadDeck(Deck).ToJsonString() },
        { "start_game", XmageRequests.StartGame(new string('a', 64), -5, 10, GameRules.Commander, watchCasts: false, log: true).ToJsonString() },
        { "start_game on the play", XmageRequests.StartGame(new string('a', 64), 7, 10, new GameRules(StartLife: 25, CommanderDamage: false, StartingPlayerDraws: false, FreeMulligans: 0), watchCasts: true, log: false).ToJsonString() },
        { "start_game interactive", XmageRequests.StartGame(new string('a', 64), 7, 10, GameRules.Commander, watchCasts: false, log: false, opponent: ["Counterspell", "Atomize", "Rout"]).ToJsonString() },
        { "action", XmageRequests.Action(3, "a:x").ToJsonString() },
        { "pass", XmageRequests.Action(3, null).ToJsonString() },
        { "keep", XmageRequests.Keep(1, true).ToJsonString() },
        { "cards", XmageRequests.Cards(2, ["Island"]).ToJsonString() },
        { "option", XmageRequests.Option(9, "none").ToJsonString() },
        { "continue", XmageRequests.Do(12, "continue").ToJsonString() },
        { "stop", XmageRequests.Do(12, "stop").ToJsonString() },
        { "counter", XmageRequests.Do(8, "counter").ToJsonString() },
        { "disrupt", XmageRequests.Disrupt(12, ["p:1"]).ToJsonString() },
        { "oppose", XmageRequests.Oppose(8, "Counterspell", ["s:1"]).ToJsonString() },
        { "oppose with no target", XmageRequests.Oppose(8, "Rout", []).ToJsonString() },
    };

    [Theory]
    [MemberData(nameof(Sent))]
    public void Every_message_the_service_sends_validates(string name, string json)
    {
        var result = Schema.Evaluate(JsonNode.Parse(json), new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, $"{name} failed the schema: {json}");
    }

    /// <summary><c>opponent</c> is required and nullable, so
    /// the idle opponent writes it as null.</summary>
    [Fact]
    public void Start_game_writes_a_null_opponent_for_the_idle_opponent_and_the_cards_for_the_scripted_one()
    {
        var idle = XmageRequests.StartGame(new string('a', 64), 1, 10, GameRules.Commander, watchCasts: false, log: false);
        var scripted = XmageRequests.StartGame(new string('a', 64), 1, 10, GameRules.Commander, watchCasts: false, log: false, opponent: ["Counterspell"]);

        Assert.EndsWith(""","opponent":null}""", idle.ToJsonString());
        Assert.EndsWith(""","opponent":{"cards":["Counterspell"]}}""", scripted.ToJsonString());
    }

    [Fact]
    public void The_service_speaks_wire_protocol_2()
    {
        Assert.Equal(2, XmageWire.Protocol);
        Assert.Equal("""{"type":"hello","protocol":2}""", XmageRequests.Hello().ToJsonString());
    }

    [Fact]
    public void A_pass_writes_action_null()
        => Assert.Equal("""{"seq":3,"action":null}""", XmageRequests.Action(3, null).ToJsonString());

    [Fact]
    public void Load_deck_names_the_commanders_and_every_other_card_with_its_quantity()
    {
        var message = XmageRequests.LoadDeck(Deck);

        Assert.Equal("""["Obeka, Brute Chronologist"]""", message["commanders"]!.ToJsonString());
        Assert.Equal("""[{"name":"Island","quantity":60},{"name":"Pithing Needle","quantity":1}]""", message["cards"]!.ToJsonString());
    }

    [Fact]
    public async Task A_frame_is_a_big_endian_length_then_utf8_json()
    {
        using var stream = new MemoryStream();
        await XmageWire.WriteFrameAsync(stream, XmageRequests.Keep(1, true), CancellationToken.None);

        var bytes = stream.ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, (byte)(bytes.Length - 4) }, bytes[..4]);
        stream.Position = 0;
        using (var frame = await XmageWire.ReadFrameAsync(stream, CancellationToken.None))
        {
            Assert.Equal("""{"seq":1,"keep":true}""", frame!.RootElement.GetRawText());
        }

        Assert.Null(await XmageWire.ReadFrameAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task A_stream_that_ends_inside_a_frame_throws()
    {
        using var stream = new MemoryStream([0, 0, 0, 9, (byte)'{']);

        await Assert.ThrowsAsync<EndOfStreamException>(() => XmageWire.ReadFrameAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task A_frame_longer_than_the_limit_throws()
    {
        using var stream = new MemoryStream([0x7f, 0xff, 0xff, 0xff]);

        await Assert.ThrowsAsync<InvalidDataException>(() => XmageWire.ReadFrameAsync(stream, CancellationToken.None));
    }

    [Fact]
    public void The_sidecar_examples_read_into_the_message_records()
    {
        var pick = Example("decide_pick");
        Assert.Equal("decide", pick.Type);
        var decide = pick.As<WireDecide>();
        Assert.Equal(9, decide.Seq);
        Assert.Equal("pick", decide.Kind);
        Assert.True(decide.View.LandDropUsed);
        Assert.Equal(["Pithing Needle", "Sol Ring"], decide.View.Finds["Demonic Tutor"]); // a card name key stays as sent
        Assert.Null(decide.Pick!.Options[1].Card);
        Assert.Equal("front", decide.View.Resolved[0].Face);
        Assert.Equal("{B}{B}{B}", decide.View.Pool);

        Assert.NotNull(Example("decide_keep").As<WireDecide>().Keep);
        Assert.NotNull(Example("decide_bottom").As<WireDecide>().Count);
        Assert.All(Example("decide_action").As<WireDecide>().View.Actions, a => Assert.False(string.IsNullOrEmpty(a.Id)));
        Assert.NotNull(Example("event_end_of_turn").As<WireEvent>().AfterDisruption);
        Assert.Equal("Pithing Needle", Example("event_cast").As<WireEvent>().Spell!.Card);
        var opposed = Example("event_opposed").As<WireEvent>();
        Assert.Equal(("Counterspell", "hit", (string?)null), (opposed.Card, opposed.Outcome, opposed.Reason));
        Assert.Equal([new WireVictim("s:1", "Pithing Needle", "front", true)], opposed.Victims);
        Assert.Equal(["Pithing Needle"], opposed.Lost);
        var notCast = Example("event_opposed_not_cast").As<WireEvent>();
        Assert.Equal(("not_cast", "no_target"), (notCast.Outcome, notCast.Reason));
        Assert.Empty(notCast.Victims!);
        var respond = Example("decide_respond").As<WireDecide>();
        Assert.Equal("respond", respond.Kind);
        Assert.Equal(new WireThreat("s:2", "Counterspell"), respond.Respond!.Threat);
        Assert.Equal([new WireRespondVictim("s:1", "Pithing Needle", "front")], respond.Respond.Victims);
        Assert.Equal("Counterspell", Assert.Single(respond.View.Actions).Card);
        Assert.Null(pick.As<WireDecide>().Respond);

        var over = Example("game_over").As<WireGameOver>();
        Assert.Equal("horizon", over.Reason);
        Assert.Equal("main", over.Log![0].Step);
        Assert.Null(over.View);
        var win = Example("game_over_win").As<WireGameOver>();
        Assert.Equal("win", win.Reason);
        Assert.Equal("Thassa's Oracle", win.View!.Resolved[0].Card); // the final view holds the winning cast
        Assert.Equal(XmageWire.Protocol, Example("hello_reply").As<WireHello>().Protocol);
        Assert.Matches("^[0-9a-f]{64}$", Example("deck").As<WireDeck>().DeckId);
    }

    [Fact]
    public void A_reply_shaped_message_has_no_type()
    {
        using var document = JsonDocument.Parse("""{"seq":1,"keep":true}""");

        Assert.Null(XmageMessage.Of(document).Type);
    }

    private static XmageMessage Example(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProtocolRoot, "examples", "valid", name + ".json")));
        return XmageMessage.Of(document);
    }
}
