using System.Text.Json.Nodes;
using Json.Schema;

namespace MtgArchive.Tests;

/// <summary>The pilot wire protocol, version 2 (ADR 0013): every
/// valid example passes the schema and every invalid one fails.</summary>
public sealed class WireSchemaTests
{
    private static readonly string ProtocolRoot = Path.Combine(RepoRoot.Find(), "engine", "xmage", "protocol");

    private static readonly JsonSchema Schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(ProtocolRoot, "pilot-wire.schema.json")));

    public static TheoryData<string> Valid() => Examples("valid");

    public static TheoryData<string> Invalid() => Examples("invalid");

    [Theory]
    [MemberData(nameof(Valid))]
    public void A_valid_example_passes(string file)
    {
        var result = Evaluate("valid", file);

        Assert.True(result.IsValid, $"{file} failed the schema.");
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_example_fails(string file)
    {
        Assert.False(Evaluate("invalid", file).IsValid, $"{file} passed the schema.");
    }

    [Fact]
    public void Every_message_shape_has_a_valid_example()
    {
        string[] expected =
        [
            "decide_action", "decide_bottom", "decide_discard", "decide_keep", "decide_pick", "decide_respond",
            "deck", "event_cast", "event_end_of_turn", "event_opposed", "event_opposed_not_cast", "event_resolved",
            "game_over", "game_over_error", "game_over_win", "hello", "hello_reply", "load_deck",
            "reply_action", "reply_cards", "reply_continue", "reply_counter", "reply_disrupt",
            "reply_keep", "reply_oppose", "reply_option", "reply_pass", "reply_stop",
            "scenario", "scenario_result", "scenario_result_error", "start_game", "start_game_interactive",
        ];

        Assert.Equal(expected, ((IEnumerable<object[]>)Valid()).Select(row => Path.GetFileNameWithoutExtension((string)row[0])));
    }

    private static EvaluationResults Evaluate(string kind, string file)
        => Schema.Evaluate(JsonNode.Parse(File.ReadAllText(Path.Combine(ProtocolRoot, "examples", kind, file))), new EvaluationOptions { OutputFormat = OutputFormat.List });

    private static TheoryData<string> Examples(string kind)
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(ProtocolRoot, "examples", kind), "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }
}
