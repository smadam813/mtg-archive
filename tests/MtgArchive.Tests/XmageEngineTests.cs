using System.Diagnostics;
using System.Text.Json.Nodes;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// The XMage adapter's game loop against the fake sidecar: one reply per
/// decide and event, the goal evaluated on each end-of-turn view, an early
/// stop once the goal is settled, a win or a loss scored on the final view,
/// and every connection failure an engine error or, when no connection
/// opens, a failed request (spec §4).
/// </summary>
public sealed class XmageEngineTests
{
    private static readonly IReadOnlyList<DeckCard> Deck =
    [
        TestDeckCards.Commander("Talrand, Sky Summoner", "{2}{U}{U}"),
        TestDeckCards.Land("Island", "U", quantity: 30),
        TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact", quantity: 4),
    ];

    private static readonly DeckFeatureProfiles Profiles = new(
        new Dictionary<string, CardFeatureProfile>
        {
            ["Island"] = new(true, false, ["{U}"], [], null, null, true),
            ["Pithing Needle"] = new(false, false, [], [], null, null, true),
        },
        ["Talrand, Sky Summoner"]);

    private static readonly GoalPredicate NeedleByOne = new GoalPredicate.OnBattlefield("Pithing Needle", 1);

    private static readonly GoalPredicate NeedleByTwo = new GoalPredicate.OnBattlefield("Pithing Needle", 2);

    private static async Task<(XmageEngine Engine, XmageDeck Deck)> PrepareAsync(XmageSidecar sidecar, int? parallelism = null)
    {
        var engine = await XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(Profiles), parallelism, CancellationToken.None);
        return (engine, (XmageDeck)await engine.PrepareAsync(Deck, CancellationToken.None));
    }

    private static RunSpec Spec(GoalPredicate goal, bool earlyStop = true, bool log = false)
        => new(0, 1234, 2, goal, null, earlyStop, log);

    private static Task<RunRecord> PlayAsync(XmageDeck deck, RunSpec spec)
        => deck.PlayAsync(spec, new GoalAwarePilot(spec.Goal!), new GoalAwareMulligan(spec.Goal!), CancellationToken.None);

    private static JsonArray NeedleBoard() => [FakeViews.Permanent("p:1", "Island", tapped: true), FakeViews.Permanent("p:2", "Pithing Needle")];

    /// <summary>Keep, play the Island, cast the Needle, see it resolve; then the test's ending, with turn 1's end-step view.</summary>
    private static Func<FakeGame, Task> NeedleOnTurnOne(Func<FakeGame, JsonObject, Task> end) => async game =>
    {
        // Three lands and the goal piece: the goal-aware mulligan keeps it.
        string[] seven = ["Island", "Island", "Island", "Pithing Needle", "Pithing Needle", "Pithing Needle", "Pithing Needle"];
        await game.AskAsync(FakeViews.Keep(FakeViews.View(0, "mulligan", hand: seven), seven));
        await game.AskAsync(FakeViews.Action(FakeViews.View(1, "main", hand: ["Island", "Pithing Needle"], actions: [FakeViews.Land("a:land", "Island")])));
        await game.AskAsync(FakeViews.Action(FakeViews.View(1, "main", hand: ["Pithing Needle"],
            battlefield: [FakeViews.Permanent("p:1", "Island")], sources: [FakeViews.Source("p:1", "{U}")],
            actions: [FakeViews.Cast("a:needle", "Pithing Needle", "{1}")])));
        await game.AskAsync(FakeViews.Resolved(FakeViews.View(1, "main", battlefield: NeedleBoard(), resolved: [FakeViews.ResolvedCast("Pithing Needle", 1)])));
        await end(game, FakeViews.View(1, "end", battlefield: NeedleBoard(), resolved: [FakeViews.ResolvedCast("Pithing Needle", 1)]));
    };

    /// <summary>Ends turn 1; on continue, ends turn 2 and reaches the horizon.</summary>
    private static async Task ToTheHorizon(FakeGame game, JsonObject endOfTurnOne)
    {
        var reply = await game.AskAsync(FakeViews.EndOfTurn(endOfTurnOne));
        if ((string?)reply["do"] == "stop")
        {
            await game.OverAsync("stopped", 1);
            return;
        }

        await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard())));
        await game.OverAsync("horizon", 2);
    }

    /// <summary>Each reply as one string: its do, action ("pass" for null), option, keep or cards.</summary>
    private static IReadOnlyList<string> Answers(FakeSidecar fake)
        => [.. fake.Replies.Select(r =>
            r.ContainsKey("do") ? (string)r["do"]!
            : r.ContainsKey("action") ? (string?)r["action"] ?? "pass"
            : r.ContainsKey("option") ? (string)r["option"]!
            : r.ContainsKey("keep") ? r["keep"]!.ToJsonString()
            : r["cards"]!.ToJsonString())];

    [Fact]
    public async Task A_settled_goal_stops_the_game_at_the_end_of_its_turn()
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne(ToTheHorizon));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Spec(NeedleByOne));

        Assert.True(record.Success);
        Assert.Equal(1, record.MetTurn);
        Assert.False(record.EngineError);
        Assert.Equal(["true", "a:land", "a:needle", "continue", "stop"], Answers(fake));
    }

    [Fact]
    public async Task An_all_of_goal_whose_leaves_hold_on_different_turns_stops_after_the_later_turn()
    {
        // The fake needs no mana: the Needle is down on turn 1, the Island on turn 2.
        await using var fake = new FakeSidecar(async game =>
        {
            var first = await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:2", "Pithing Needle")])));
            if ((string?)first["do"] == "stop")
            {
                await game.OverAsync("stopped", 1);
                return;
            }

            var second = await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard())));
            await game.OverAsync((string?)second["do"] == "stop" ? "stopped" : "horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);
        var goal = new GoalPredicate.AllOf([NeedleByOne, new GoalPredicate.OnBattlefield("Island", 2)]);

        var record = await PlayAsync(deck, Spec(goal));

        Assert.True(record.Success);
        Assert.Equal(2, record.MetTurn);
        Assert.Equal(["continue", "stop"], Answers(fake));
    }

    [Fact]
    public async Task Without_early_stop_the_game_runs_to_the_horizon()
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne(ToTheHorizon));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Spec(NeedleByOne, earlyStop: false));

        Assert.True(record.Success);
        Assert.Equal(["true", "a:land", "a:needle", "continue", "continue", "continue"], Answers(fake));
    }

    [Fact]
    public async Task Start_game_carries_the_deck_the_seed_the_horizon_the_commander_game_rules_and_the_log_flag()
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne(ToTheHorizon));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        await PlayAsync(deck, Spec(NeedleByOne, log: true));

        var start = fake.Received.Single(m => (string?)m["type"] == "start_game");
        Assert.Equal(FakeSidecar.DeckId, (string?)start["deck_id"]);
        Assert.Equal(1234, (int)start["seed"]!);
        Assert.Equal(2, (int)start["horizon"]!);
        Assert.Equal(40, (int)start["start_life"]!);
        Assert.True((bool)start["commander_damage"]!);
        Assert.True((bool)start["starting_player_draws"]!);
        Assert.Equal(1, (int)start["free_mulligans"]!);
        Assert.False((bool)start["watch_casts"]!);
        Assert.True((bool)start["log"]!);
        Assert.True(start.ContainsKey("opponent"));
        Assert.Null(start["opponent"]);
    }

    /// <summary>An abstracted profile seats the idle opponent;
    /// an interactive one seats the scripted opponent with the card table.</summary>
    [Theory]
    [InlineData(DisruptionMode.Abstracted, null)]
    [InlineData(DisruptionMode.Interactive, """{"cards":["Counterspell","Atomize","Rout"]}""")]
    public async Task Start_game_names_the_opponent_of_the_profiles_mode(DisruptionMode mode, string? opponent)
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne(ToTheHorizon));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        await PlayAsync(deck, Tier3(NeedleByOne, new DisruptionProfile([], mode), horizon: 2));

        var start = StartOf(fake);
        Assert.True(start.ContainsKey("opponent"));
        Assert.Equal(opponent, start["opponent"]?.ToJsonString());
    }

    [Fact]
    public async Task The_log_puts_each_turns_goal_lines_after_its_end_snapshot()
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne(async (game, end) =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(end));
            await game.OverAsync("stopped", 1, log:
            [
                FakeViews.Log(1, "main", "PILOT land Island"),
                FakeViews.Log(1, "end", "END life=40 lib=90 hand=[] bf=[Island(T), Pithing Needle] gy=[]"),
                FakeViews.Log(1, "cleanup", "STOP"),
            ]);
        }));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Spec(NeedleByOne, log: true));

        var messages = record.Log!.Select(l => l.Message).ToList();
        Assert.Equal("PILOT land Island", messages[0]);
        Assert.StartsWith("END life=40", messages[1]);
        Assert.StartsWith("Goal ", messages[2]);
        Assert.Equal("STOP", messages[^1]);
        Assert.Equal(TurnStep.Cleanup, record.Log![^1].Step);
    }

    [Theory]
    [InlineData("win")]
    [InlineData("life")]
    public async Task A_game_that_ends_inside_a_turn_scores_the_goal_on_the_final_view(string reason)
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne((game, end) => game.OverAsync(reason, 1, view: end)));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var byOne = await PlayAsync(deck, Spec(NeedleByOne));
        var byTwo = await PlayAsync(deck, Spec(NeedleByTwo));

        Assert.True(byOne.Success);
        Assert.Equal(1, byOne.MetTurn);
        Assert.False(byTwo.Success); // the leaf is due on turn 2, after the game ended
        Assert.False(byTwo.EngineError);
    }

    /// <summary>Keep, play the Island, cast the Needle; then the test's ending. No event carries the resolved Needle.</summary>
    private static Func<FakeGame, Task> CastNeedleThen(Func<FakeGame, Task> end) => async game =>
    {
        string[] seven = ["Island", "Island", "Island", "Pithing Needle", "Pithing Needle", "Pithing Needle", "Pithing Needle"];
        await game.AskAsync(FakeViews.Keep(FakeViews.View(0, "mulligan", hand: seven), seven));
        await game.AskAsync(FakeViews.Action(FakeViews.View(1, "main", hand: ["Island", "Pithing Needle"], actions: [FakeViews.Land("a:land", "Island")])));
        await game.AskAsync(FakeViews.Action(FakeViews.View(1, "main", hand: ["Pithing Needle"],
            battlefield: [FakeViews.Permanent("p:1", "Island")], sources: [FakeViews.Source("p:1", "{U}")],
            actions: [FakeViews.Cast("a:needle", "Pithing Needle", "{1}")])));
        await end(game);
    };

    [Fact]
    public async Task A_win_scores_the_winning_cast_that_only_the_final_view_holds()
    {
        // As on the real sidecar: the win trigger is on the stack when the spell resolves, so the
        // last decide view is the cast decision and only game_over's final view holds the cast.
        await using var fake = new FakeSidecar(CastNeedleThen(game => game.OverAsync("win", 1,
            view: FakeViews.View(1, "main", battlefield: NeedleBoard(), resolved: [FakeViews.ResolvedCast("Pithing Needle", 1)]))));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Spec(new GoalPredicate.Cast("Pithing Needle", 1)));

        Assert.True(record.Success);
        Assert.Equal(1, record.MetTurn);
        Assert.False(record.EngineError);
    }

    [Fact]
    public async Task A_win_without_a_final_view_is_an_engine_error()
    {
        await using var fake = new FakeSidecar(CastNeedleThen(game => game.OverAsync("win", 1)));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Spec(new GoalPredicate.Cast("Pithing Needle", 1)));

        Assert.True(record.EngineError);
        Assert.False(record.Success);
    }

    [Fact]
    public async Task An_engine_error_is_a_failure_and_the_connection_plays_on()
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne((game, _) => game.OverAsync("engine_error", 1, error: "boom")));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var first = await PlayAsync(deck, Spec(NeedleByOne, log: true));
        var second = await PlayAsync(deck, Spec(NeedleByOne));

        Assert.True(first.EngineError);
        Assert.False(first.Success);
        Assert.Null(first.MetTurn);
        Assert.Contains(first.Log!, l => l.Message.Contains("boom", StringComparison.Ordinal));
        Assert.True(second.EngineError);
        Assert.Equal(1, fake.Connections);
    }

    [Fact]
    public async Task A_skipped_seq_is_an_engine_error_that_closes_the_connection()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            var keep = FakeViews.Keep(FakeViews.View(0, "mulligan", hand: ["Island"]), ["Island"]);
            keep["seq"] = 5;
            await game.SendAsync(keep);
            await game.WaitClosedAsync();
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Spec(NeedleByOne));
        await PlayAsync(deck, Spec(NeedleByOne));

        Assert.True(record.EngineError);
        Assert.Equal(2, fake.Connections); // the second game opened a new connection
    }

    [Fact]
    public async Task A_silent_sidecar_is_an_engine_error_after_the_read_timeout()
    {
        await using var fake = new FakeSidecar(game => game.WaitClosedAsync());
        await using var sidecar = new XmageSidecar(fake.Options(readTimeoutSeconds: 0.5));
        var (_, deck) = await PrepareAsync(sidecar);

        var clock = Stopwatch.StartNew();
        var record = await PlayAsync(deck, Spec(NeedleByOne));

        Assert.True(record.EngineError);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), clock.Elapsed.ToString());
    }

    [Fact]
    public async Task A_cancelled_game_closes_its_connection_and_the_next_game_opens_a_new_one()
    {
        var games = 0;
        var closed = new TaskCompletionSource();
        await using var fake = new FakeSidecar(async game =>
        {
            if (Interlocked.Increment(ref games) > 1)
            {
                await NeedleOnTurnOne(ToTheHorizon)(game);
                return;
            }

            await game.WaitClosedAsync(); // silent until the service closes the connection
            closed.TrySetResult();
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);
        var spec = Spec(NeedleByOne);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => deck.PlayAsync(spec, new GoalAwarePilot(NeedleByOne), new GoalAwareMulligan(NeedleByOne), cancel.Token));
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(2)); // closed, not back in the pool
        var next = await PlayAsync(deck, spec);

        Assert.True(next.Success);
        Assert.Equal(2, fake.Connections); // the cancelled read may have left the stream mid-frame
    }

    [Fact]
    public async Task A_pick_answers_with_the_wire_id_and_a_stop_goes_back_as_none()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.Pick(FakeViews.View(1, "main"), "tutor", null, ("o1", "Pithing Needle", "Pithing Needle"), ("none", "Find nothing", null)));
            await game.AskAsync(FakeViews.Pick(FakeViews.View(1, "draw"), "scry", ["Island"], ("o1", "Island", "Island"), ("none", "Keep the rest on top", null)));
            await game.OverAsync("stopped", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        await PlayAsync(deck, Spec(NeedleByOne));

        // The tutor takes the goal piece; the scry keeps the only land when the hand holds none.
        Assert.Equal(["o1", "none"], Answers(fake));
    }

    [Fact]
    public async Task Bottom_and_discard_name_cards_from_the_hand()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.Count("bottom", FakeViews.View(0, "mulligan", hand: ["Island", "Island", "Pithing Needle"]), 1));
            await game.AskAsync(FakeViews.Count("discard", FakeViews.View(1, "cleanup", hand: ["Island", "Pithing Needle"]), 1));
            await game.OverAsync("stopped", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        await PlayAsync(deck, Spec(NeedleByOne));

        var replies = fake.Replies.ToList();
        Assert.Equal("""["Island"]""", replies[0]["cards"]!.ToJsonString());
        Assert.Equal("""["Island"]""", replies[1]["cards"]!.ToJsonString());
    }

    private static DisruptionProfile Profile(params (DisruptionKind Kind, int Turn)[] events)
        => new([.. events.Select(e => new DisruptionEvent(e.Kind, e.Turn))]);

    private static RunSpec Tier3(GoalPredicate goal, DisruptionProfile profile, int horizon = 1)
        => new(0, 1234, horizon, goal, profile, EarlyStop: false, CaptureLog: true);

    private static JsonObject StartOf(FakeSidecar fake) => fake.Received.Single(m => (string?)m["type"] == "start_game");

    [Fact]
    public async Task A_counterspell_counters_the_cast_the_goal_wants()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(1, "main"), "s:1", "Pithing Needle"));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", graveyard: ["Pithing Needle"])));
            await game.OverAsync("horizon", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Profile((DisruptionKind.Counterspell, 1))));

        Assert.True((bool)StartOf(fake)["watch_casts"]!);
        Assert.Equal(["counter", "continue"], Answers(fake));
        Assert.False(record.Success);
        var outcome = Assert.Single(record.Disruption!);
        Assert.Equal(1, outcome.FiredTurn);
        Assert.Equal(["Pithing Needle"], outcome.Victims);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: counterspell fires on Pithing Needle.");
    }

    /// <summary>Phase 3A, Review Focus 1: a cast the goal does not want resolves while a wanted card is still in the library.</summary>
    [Fact]
    public async Task A_counterspell_holds_while_a_wanted_card_is_still_in_the_library()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(4, "main"), "s:1", "Talrand, Sky Summoner"));
            await game.OverAsync("horizon", 4);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByTwo, Profile((DisruptionKind.Counterspell, 1)), horizon: 4));

        Assert.Equal(["continue"], Answers(fake));
        Assert.True(Assert.Single(record.Disruption!).Unused);
    }

    /// <summary>Phase 3A, Review Focus 1: with every Needle seen, the commander on the stack is the only target left.</summary>
    [Fact]
    public async Task A_counterspell_takes_any_cast_once_no_wanted_card_is_reachable()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            string[] needles = ["Pithing Needle", "Pithing Needle", "Pithing Needle", "Pithing Needle"];
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(4, "main", graveyard: needles), "s:1", "Talrand, Sky Summoner"));
            await game.OverAsync("horizon", 4);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByTwo, Profile((DisruptionKind.Counterspell, 1)), horizon: 4));

        Assert.Equal(["counter"], Answers(fake));
        Assert.Equal(["Talrand, Sky Summoner"], Assert.Single(record.Disruption!).Victims);
    }

    [Fact]
    public async Task A_removal_answers_disrupt_and_the_goal_reads_the_after_view()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard())));
            await game.AskAsync(FakeViews.EndOfTurn(
                FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)], graveyard: ["Pithing Needle"]),
                afterDisruption: true));
            await game.OverAsync("horizon", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Profile((DisruptionKind.TargetedRemoval, 1))));

        Assert.False((bool)StartOf(fake)["watch_casts"]!);
        Assert.Equal(["disrupt", "continue"], Answers(fake));
        Assert.Equal("""["p:2"]""", fake.Replies.First()["remove"]!.ToJsonString()); // the Needle, not the Island
        Assert.False(record.Success); // the after view has no Needle
        var outcome = Assert.Single(record.Disruption!);
        Assert.Equal(1, outcome.FiredTurn);
        Assert.Equal(["Pithing Needle"], outcome.Victims);
    }

    /// <summary>The goal reads the after view, so its lines follow the after snapshot, not the one before the removal.</summary>
    [Fact]
    public async Task After_a_disruption_the_log_puts_the_turns_lines_after_the_after_snapshot()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard())));
            await game.AskAsync(FakeViews.EndOfTurn(
                FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)], graveyard: ["Pithing Needle"]),
                afterDisruption: true));
            await game.OverAsync("horizon", 1, log:
            [
                FakeViews.Log(1, "end", "END life=40 lib=90 hand=[] bf=[Island(T), Pithing Needle] gy=[]"),
                FakeViews.Log(1, "end", "DISRUPT remove Pithing Needle"),
                FakeViews.Log(1, "end", "END life=40 lib=90 hand=[] bf=[Island(T)] gy=[Pithing Needle]"),
            ]);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Profile((DisruptionKind.TargetedRemoval, 1))));

        var messages = record.Log!.Select(l => l.Message).ToList();
        Assert.Equal("DISRUPT remove Pithing Needle", messages[1]);
        Assert.StartsWith("END life=40 lib=90 hand=[] bf=[Island(T)]", messages[2]);
        Assert.StartsWith("Disruption: ", messages[3]);
        Assert.StartsWith("Goal ", messages[4]);
    }

    [Fact]
    public async Task A_counterspell_logs_its_fire_at_the_step_of_the_cast()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(1, "upkeep"), "s:1", "Pithing Needle"));
            await game.OverAsync("horizon", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Profile((DisruptionKind.Counterspell, 1))));

        Assert.Equal(TurnStep.Upkeep, Assert.Single(record.Log!, l => l.Message.StartsWith("Disruption:", StringComparison.Ordinal)).Step);
    }

    [Fact]
    public async Task An_end_of_turn_with_no_victim_scores_the_goal_at_once()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island")])));
            await game.OverAsync("horizon", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);
        var island = new GoalPredicate.OnBattlefield("Island", 1);

        var record = await deck.PlayAsync(Tier3(island, Profile((DisruptionKind.TargetedRemoval, 1))), new GoalAwarePilot(island), new GoalAwareMulligan(island), CancellationToken.None);

        Assert.Equal(["continue"], Answers(fake));
        Assert.True(record.Success);
        Assert.True(Assert.Single(record.Disruption!).Unused);
    }

    [Fact]
    public async Task An_after_view_with_no_disruption_before_it_is_an_engine_error()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end"), afterDisruption: true));
            await game.WaitClosedAsync();
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Profile((DisruptionKind.TargetedRemoval, 1))));

        Assert.True(record.EngineError);
        Assert.Single(record.Disruption!);
    }

    [Fact]
    public async Task A_normal_end_of_turn_where_the_after_view_was_due_is_an_engine_error()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard())));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard()), afterDisruption: false));
            await game.WaitClosedAsync();
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Profile((DisruptionKind.TargetedRemoval, 1))));

        Assert.True(record.EngineError);
        Assert.Single(record.Disruption!);
    }

    [Fact]
    public async Task A_horizon_before_the_after_view_is_an_engine_error()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard())));
            await game.OverAsync("horizon", 1);
            await game.WaitClosedAsync();
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Profile((DisruptionKind.TargetedRemoval, 1))));

        Assert.True(record.EngineError);
        Assert.Single(record.Disruption!);
    }

    /// <summary>Phase 3A, Review Focus 4: engine errors under a profile still aggregate.</summary>
    [Fact]
    public async Task A_paired_batch_of_engine_errors_keeps_one_outcome_per_event()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("engine_error", 0, error: "boom"));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (engine, deck) = await PrepareAsync(sidecar);
        var profile = Profile((DisruptionKind.Counterspell, 1), (DisruptionKind.TargetedRemoval, 2));

        var paired = await MonteCarlo.RunPairedAsync(
            engine, deck, new GoalAwarePilot(NeedleByOne), new GoalAwareMulligan(NeedleByOne),
            new MonteCarloOptions(Runs: 5, BaseSeed: 1, TurnHorizon: 2, Goal: NeedleByOne, Disruption: profile));

        Assert.Equal(5, paired.Disrupted.EngineErrors);
        Assert.Equal(2, paired.Events.Count);
        Assert.All(paired.Events, e => Assert.True(e.Unused));
    }

    // ------------------------------------------------------------ interactive disruption

    private static DisruptionProfile Interactive(params (DisruptionKind Kind, int Turn)[] events)
        => Profile(events) with { Mode = DisruptionMode.Interactive };

    /// <summary>The unchanged tier-3 predicate proposes, the service answers
    /// oppose with the cast spell as the only target, and the schedule
    /// commits on the opposed event.</summary>
    [Fact]
    public async Task An_interactive_counterspell_opposes_the_cast_and_commits_on_a_hit()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(1, "main"), "s:1", "Pithing Needle"));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "main", graveyard: ["Pithing Needle"]), "Counterspell", "hit",
                victims: [FakeViews.Victim("s:1", "Pithing Needle", hit: true)], lost: ["Pithing Needle"]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", graveyard: ["Pithing Needle"])));
            await game.OverAsync("horizon", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Interactive((DisruptionKind.Counterspell, 1))));

        var start = StartOf(fake);
        Assert.True((bool)start["watch_casts"]!);
        Assert.NotNull(start["opponent"]);
        Assert.Equal(["oppose", "continue", "continue"], Answers(fake));
        var oppose = fake.Replies.First();
        Assert.Equal("Counterspell", (string?)oppose["card"]);
        Assert.Equal("""["s:1"]""", oppose["targets"]!.ToJsonString());
        Assert.False(record.EngineError);
        Assert.False(record.Success);
        var outcome = Assert.Single(record.Disruption!);
        Assert.Equal(1, outcome.FiredTurn);
        Assert.Equal([new VictimHit("Pithing Needle", true)], outcome.Hits);
        Assert.False(outcome.NotCast);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: counterspell casts Counterspell on Pithing Needle; it hits.");
        Assert.Equal([new ExemplarEvent(1, "counterspell", "Pithing Needle", true, [])], MonteCarlo.EventsOf(record.Disruption));
    }

    // ------------------------------------------------------------ the response window

    private static readonly CardFeatureProfile HardCounter = new(false, false, [], [], null, null, false,
        new ResponseFeature(new CounterFeature(CounterReach.Spell, [], [], [], null, false), []));

    /// <summary>The deck with four Counterspells, whose profile proves a hard counter.</summary>
    private static readonly IReadOnlyList<DeckCard> AnswerDeck = [.. Deck, TestDeckCards.Spell("Counterspell", "{U}{U}", "Instant", quantity: 4)];

    private static readonly DeckFeatureProfiles AnswerProfiles = new(
        new Dictionary<string, CardFeatureProfile>(Profiles.ByFace) { ["Counterspell"] = HardCounter },
        Profiles.Unprofiled,
        ["Counterspell"],
        []);

    private static async Task<XmageDeck> PrepareAnswersAsync(XmageSidecar sidecar)
    {
        var engine = await XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(AnswerProfiles), null, CancellationToken.None);
        return (XmageDeck)await engine.PrepareAsync(AnswerDeck, CancellationToken.None);
    }

    /// <summary>The window with a Counterspell to cast and two Islands up.</summary>
    private static JsonObject WindowView() => FakeViews.View(2, "main", hand: ["Counterspell"],
        battlefield: [FakeViews.Permanent("p:1", "Island"), FakeViews.Permanent("p:3", "Island")],
        actions: [FakeViews.Cast("a:cs", "Counterspell", "{U}{U}")]);

    /// <summary>The goal-aware pilot answers the opponent's Counterspell on
    /// a goal spell with its own; answered_by names the card of its reply,
    /// and the service logs it.</summary>
    [Fact]
    public async Task A_respond_decide_reaches_the_pilot_and_answered_by_comes_from_its_reply()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(2, "main"), "s:1", "Pithing Needle"));
            await game.AskAsync(FakeViews.Respond(WindowView(), "s:9", "Counterspell", ("s:1", "Pithing Needle")));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(2, "main", battlefield: NeedleBoard()), "Counterspell", "answered",
                victims: [FakeViews.Victim("s:1", "Pithing Needle", hit: false)]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard())));
            await game.OverAsync("horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var deck = await PrepareAnswersAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByTwo, Interactive((DisruptionKind.Counterspell, 1)), horizon: 2));

        Assert.False(record.EngineError);
        Assert.Equal(["oppose", "a:cs", "continue", "continue"], Answers(fake));
        Assert.True(record.Success);
        var outcome = Assert.Single(record.Disruption!);
        Assert.Equal(2, outcome.FiredTurn);
        Assert.True(outcome.Answered);
        Assert.Equal(["Counterspell"], outcome.AnsweredBy);
        Assert.Equal([new VictimHit("Pithing Needle", false)], outcome.Hits);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: counterspell casts Counterspell on Pithing Needle; answered by Counterspell.");
        Assert.Equal([new ExemplarEvent(2, "counterspell", "Pithing Needle", false, ["Counterspell"])], MonteCarlo.EventsOf(record.Disruption));
    }

    /// <summary>The adapter maps the threat through the disruption card
    /// table and the victims through the deck. A second respond decide for
    /// the same card follows a failed activation: its reply replaces the
    /// first. A pass as the last reply means our deck did not respond: the
    /// card that leaves the stack is fired, not answered, and hits nothing.</summary>
    [Fact]
    public async Task The_threat_maps_through_the_card_table_and_a_retried_window_keeps_the_last_reply()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(2, "main"), "s:1", "Pithing Needle"));
            await game.AskAsync(FakeViews.Respond(WindowView(), "s:9", "Counterspell", ("s:1", "Pithing Needle")));
            await game.AskAsync(FakeViews.Respond(FakeViews.View(2, "main"), "s:9", "Counterspell", ("s:1", "Pithing Needle")));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(2, "main"), "Counterspell", "answered",
                victims: [FakeViews.Victim("s:1", "Pithing Needle", hit: false)]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end")));
            await game.OverAsync("horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var deck = await PrepareAnswersAsync(sidecar);
        var pilot = new ScriptedViewPilot { Answer = (view, _) => view.Actions.FirstOrDefault()?.Id };

        var record = await deck.PlayAsync(Tier3(NeedleByTwo, Interactive((DisruptionKind.Counterspell, 1)), horizon: 2), pilot, new GoalAwareMulligan(NeedleByTwo), CancellationToken.None);

        Assert.False(record.EngineError);
        Assert.Equal(["oppose", "a:cs", "pass", "continue", "continue"], Answers(fake));
        Assert.Equal(2, pilot.Responses.Count);
        var (view, choice) = pilot.Responses[0];
        Assert.Same(DisruptionCards.Counterspell, choice.Threat);
        Assert.Equal("s:9", choice.ThreatId);
        var victim = Assert.Single(choice.Victims);
        Assert.Equal("s:1", victim.Id);
        Assert.Same(AnswerDeck[2], victim.Card);
        Assert.Same(HardCounter, Assert.Single(view.Actions).Features);
        var outcome = Assert.Single(record.Disruption!);
        Assert.Equal(2, outcome.FiredTurn);
        Assert.False(outcome.Answered);
        Assert.Null(outcome.AnsweredBy);
        Assert.Equal([new VictimHit("Pithing Needle", false)], outcome.Hits);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: counterspell casts Counterspell on Pithing Needle; it takes no victim.");
        Assert.Equal([new ExemplarEvent(2, "counterspell", "Pithing Needle", false, [])], MonteCarlo.EventsOf(record.Disruption));
    }

    /// <summary>A pilot whose response does not stop the card leaves the
    /// outcome a hit, with no answered_by.</summary>
    [Fact]
    public async Task A_hit_after_a_response_records_no_answer()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(2, "main"), "s:1", "Pithing Needle"));
            await game.AskAsync(FakeViews.Respond(WindowView(), "s:9", "Counterspell", ("s:1", "Pithing Needle")));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(2, "main"), "Counterspell", "hit",
                victims: [FakeViews.Victim("s:1", "Pithing Needle", hit: true)], lost: ["Pithing Needle"]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end")));
            await game.OverAsync("horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var deck = await PrepareAnswersAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByTwo, Interactive((DisruptionKind.Counterspell, 1)), horizon: 2));

        var outcome = Assert.Single(record.Disruption!);
        Assert.False(outcome.Answered);
        Assert.Null(outcome.AnsweredBy);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: counterspell casts Counterspell on Pithing Needle; it hits.");
    }

    /// <summary>A card that was not cast commits nothing: the event stays
    /// armed, and the next qualifying cast is opposed again.</summary>
    [Fact]
    public async Task A_not_cast_outcome_leaves_the_event_armed_for_the_next_fire_point()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(1, "main"), "s:1", "Pithing Needle"));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "main"), "Counterspell", "not_cast", reason: "no_target"));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard())));
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(2, "main", battlefield: NeedleBoard()), "s:2", "Pithing Needle"));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(2, "main", battlefield: NeedleBoard()), "Counterspell", "not_cast", reason: "cannot_cast",
                victims: [FakeViews.Victim("s:2", "Pithing Needle", hit: false)]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard())));
            await game.OverAsync("horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByTwo, Interactive((DisruptionKind.Counterspell, 1)), horizon: 2));

        Assert.Equal(["oppose", "continue", "continue", "oppose", "continue", "continue"], Answers(fake));
        Assert.False(record.EngineError);
        var outcome = Assert.Single(record.Disruption!);
        Assert.True(outcome.Unused);
        Assert.True(outcome.NotCast);
        Assert.Equal("cannot_cast", outcome.NotCastReason);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: counterspell casts Counterspell on Pithing Needle; it cannot be cast (no target).");
        Assert.Contains(record.Log!, l => l.Message == "Disruption: counterspell casts Counterspell on Pithing Needle; it cannot be cast (cannot cast).");
        Assert.Equal([], MonteCarlo.EventsOf(record.Disruption)!);
    }

    /// <summary>In an interactive game the service never answers counter
    /// or disrupt: the shim refuses both with a non-null opponent.</summary>
    [Fact]
    public async Task An_interactive_game_never_answers_counter_or_disrupt()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(1, "main"), "s:1", "Pithing Needle"));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "main"), "Counterspell", "not_cast", reason: "no_target"));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard())));
            await game.OverAsync("horizon", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Interactive((DisruptionKind.Counterspell, 1))));

        Assert.DoesNotContain("counter", Answers(fake));
        Assert.DoesNotContain("disrupt", Answers(fake));
        Assert.True(record.Success); // the Needle stands on the end-of-turn view
    }

    /// <summary>The shim's refusal of counter or disrupt with a non-null
    /// opponent ends the run as an engine error, with one outcome per event.</summary>
    [Fact]
    public async Task The_shims_refusal_of_counter_with_an_opponent_is_an_engine_error()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("engine_error", 1, error: "bad reply: the cast event takes do=continue, oppose, stop, got counter"));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Interactive((DisruptionKind.Counterspell, 1))));

        Assert.True(record.EngineError);
        Assert.Single(record.Disruption!);
    }

    public static TheoryData<string> InteractiveViolations => new()
    {
        "opposed with nothing pending",
        "cast while pending",
        "game_over while pending",
        "end_of_turn while pending",
        "opposed for another card",
        "reason on a hit",
        "respond with nothing pending",
        "respond for another card",
    };

    /// <summary>Each protocol violation of the opposed exchange is an
    /// engine error, and the record keeps one outcome per event.</summary>
    [Theory]
    [MemberData(nameof(InteractiveViolations))]
    public async Task An_interactive_protocol_violation_is_an_engine_error(string violation)
    {
        await using var fake = new FakeSidecar(async game =>
        {
            if (violation == "opposed with nothing pending")
            {
                await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "main"), "Counterspell", "hit"));
                await game.WaitClosedAsync();
                return;
            }

            if (violation == "respond with nothing pending")
            {
                await game.AskAsync(FakeViews.Respond(FakeViews.View(1, "main"), "s:9", "Counterspell", ("s:1", "Pithing Needle")));
                await game.WaitClosedAsync();
                return;
            }

            await game.AskAsync(FakeViews.CastEvent(FakeViews.View(1, "main"), "s:1", "Pithing Needle"));
            switch (violation)
            {
                case "cast while pending":
                    await game.AskAsync(FakeViews.CastEvent(FakeViews.View(1, "main"), "s:2", "Pithing Needle"));
                    break;
                case "game_over while pending":
                    await game.OverAsync("horizon", 1);
                    break;
                case "end_of_turn while pending":
                    await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end")));
                    break;
                case "opposed for another card":
                    await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "main"), "Atomize", "hit"));
                    break;
                case "reason on a hit":
                    await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "main"), "Counterspell", "hit", reason: "no_target"));
                    break;
                case "respond for another card":
                    await game.AskAsync(FakeViews.Respond(FakeViews.View(1, "main"), "s:9", "Atomize", ("s:1", "Pithing Needle")));
                    break;
            }

            await game.WaitClosedAsync();
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Interactive((DisruptionKind.Counterspell, 1))));

        Assert.True(record.EngineError, violation);
        Assert.Single(record.Disruption!);
    }

    // ------------------------------------------------------------ interactive removal

    /// <summary>The Needle and Talrand on the battlefield beside a tapped Island.</summary>
    private static JsonArray TalrandBoard() =>
    [
        FakeViews.Permanent("p:1", "Island", tapped: true),
        FakeViews.Permanent("p:2", "Pithing Needle"),
        FakeViews.Permanent("p:3", "Talrand, Sky Summoner"),
    ];

    /// <summary>The end-of-turn event takes oppose with the removal order
    /// as targets: the goal's Needle, then Talrand, never the land. The
    /// schedule commits on the opposed event, and the goal reads the after
    /// view.</summary>
    [Fact]
    public async Task An_interactive_removal_opposes_at_the_end_step_and_the_goal_reads_the_after_view()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: TalrandBoard())));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true), FakeViews.Permanent("p:3", "Talrand, Sky Summoner")], graveyard: ["Pithing Needle"]),
                "Atomize", "hit", victims: [FakeViews.Victim("p:2", "Pithing Needle", hit: true)], lost: ["Pithing Needle"]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)], graveyard: ["Pithing Needle"]), afterDisruption: true));
            await game.OverAsync("horizon", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Interactive((DisruptionKind.TargetedRemoval, 1))));

        Assert.False(record.EngineError);
        Assert.False((bool)StartOf(fake)["watch_casts"]!);
        Assert.NotNull(StartOf(fake)["opponent"]);
        Assert.Equal(["oppose", "continue", "continue"], Answers(fake));
        var oppose = fake.Replies.First();
        Assert.Equal("Atomize", (string?)oppose["card"]);
        Assert.Equal("""["p:2","p:3"]""", oppose["targets"]!.ToJsonString());
        Assert.False(record.Success); // the after view has no Needle
        var outcome = Assert.Single(record.Disruption!);
        Assert.Equal(1, outcome.FiredTurn);
        Assert.Equal([new VictimHit("Pithing Needle", true)], outcome.Hits);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: targeted_removal casts Atomize on Pithing Needle; it hits.");
        Assert.Equal([new ExemplarEvent(1, "targeted_removal", "Pithing Needle", true, [])], MonteCarlo.EventsOf(record.Disruption));
    }

    /// <summary>The end-step chain: after the first opposed event, the
    /// service proposes the next armed removal on the settled view; after
    /// the last one, the after view is the goal read.</summary>
    [Fact]
    public async Task The_end_step_chain_proposes_the_next_armed_event_on_the_settled_view()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: TalrandBoard())));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true), FakeViews.Permanent("p:3", "Talrand, Sky Summoner")]),
                "Atomize", "hit", victims: [FakeViews.Victim("p:2", "Pithing Needle", hit: true)], lost: ["Pithing Needle"]));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)], commandZone: ["Talrand, Sky Summoner"]),
                "Atomize", "hit", victims: [FakeViews.Victim("p:3", "Talrand, Sky Summoner", hit: true)], lost: ["Talrand, Sky Summoner"]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)]), afterDisruption: true));
            await game.OverAsync("horizon", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Interactive((DisruptionKind.TargetedRemoval, 1), (DisruptionKind.TargetedRemoval, 1))));

        Assert.False(record.EngineError);
        Assert.Equal(["oppose", "oppose", "continue", "continue"], Answers(fake));
        Assert.Equal("""["p:3"]""", fake.Replies.ElementAt(1)["targets"]!.ToJsonString()); // the settled view, not the first one
        Assert.Equal([1, 1], record.Disruption!.Select(o => o.FiredTurn));
        Assert.Equal(["Pithing Needle", "Talrand, Sky Summoner"], record.Disruption!.Select(o => Assert.Single(o.Victims)));
    }

    /// <summary>A removal that was not cast commits nothing. The chain does
    /// not propose it again in the same end step; the after view follows,
    /// and the next end step proposes it again.</summary>
    [Fact]
    public async Task A_removal_that_was_not_cast_stays_armed_for_the_next_end_step()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard())));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "end", battlefield: NeedleBoard()), "Atomize", "not_cast", reason: "no_target"));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard()), afterDisruption: true));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard())));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(2, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)]), "Atomize", "hit",
                victims: [FakeViews.Victim("p:2", "Pithing Needle", hit: true)], lost: ["Pithing Needle"]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)]), afterDisruption: true));
            await game.OverAsync("horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByTwo, Interactive((DisruptionKind.TargetedRemoval, 1)), horizon: 2));

        Assert.False(record.EngineError);
        Assert.Equal(["oppose", "continue", "continue", "oppose", "continue", "continue"], Answers(fake));
        Assert.False(record.Success);
        var outcome = Assert.Single(record.Disruption!);
        Assert.Equal(2, outcome.FiredTurn);
        Assert.False(outcome.NotCast);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: targeted_removal casts Atomize on Pithing Needle; it cannot be cast (no target).");
    }

    /// <summary>The goal-aware pilot answers Atomize on its goal permanent
    /// with Counterspell; answered_by names it, and the Needle stands on
    /// the after view.</summary>
    [Fact]
    public async Task The_pilot_answers_atomize_on_a_goal_permanent_in_the_end_step_window()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard())));
            await game.AskAsync(FakeViews.Respond(FakeViews.View(2, "end", hand: ["Counterspell"], battlefield: NeedleBoard(),
                actions: [FakeViews.Cast("a:cs", "Counterspell", "{U}{U}")]), "s:9", "Atomize", ("p:2", "Pithing Needle")));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(2, "end", battlefield: NeedleBoard()), "Atomize", "answered",
                victims: [FakeViews.Victim("p:2", "Pithing Needle", hit: false)]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard()), afterDisruption: true));
            await game.OverAsync("horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var deck = await PrepareAnswersAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByTwo, Interactive((DisruptionKind.TargetedRemoval, 1)), horizon: 2));

        Assert.False(record.EngineError);
        Assert.Equal(["oppose", "a:cs", "continue", "continue"], Answers(fake));
        Assert.True(record.Success);
        var outcome = Assert.Single(record.Disruption!);
        Assert.Equal(["Counterspell"], outcome.AnsweredBy);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: targeted_removal casts Atomize on Pithing Needle; answered by Counterspell.");
    }

    private static readonly IReadOnlyList<DeckCard> GrantDeck =
    [
        .. Deck,
        TestDeckCards.Spell("Snakeskin Veil", "{G}", "Instant", quantity: 4),
        TestDeckCards.Spell("Heroic Intervention", "{1}{G}", "Instant", quantity: 4),
    ];

    private static readonly DeckFeatureProfiles GrantProfiles = new(
        new Dictionary<string, CardFeatureProfile>(Profiles.ByFace)
        {
            ["Snakeskin Veil"] = CardFeatureProfile.None with { Response = new ResponseFeature(null, [new ProtectionGrant(ProtectionGrant.Hexproof, ProtectionGrant.TargetCreature)]) },
            ["Heroic Intervention"] = CardFeatureProfile.None with
            {
                Response = new ResponseFeature(null, [new ProtectionGrant(ProtectionGrant.Hexproof, ProtectionGrant.Permanents), new ProtectionGrant(ProtectionGrant.Indestructible, ProtectionGrant.Permanents)]),
            },
        },
        Profiles.Unprofiled,
        ["Heroic Intervention", "Snakeskin Veil"],
        []);

    /// <summary>The pilot answers Atomize on its goal Needle with
    /// Heroic Intervention. Snakeskin Veil is cheaper, but its scope is a
    /// target creature, and the Needle is an artifact.</summary>
    [Fact]
    public async Task The_pilot_answers_atomize_with_a_grant_whose_scope_covers_the_victim()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard())));
            await game.AskAsync(FakeViews.Respond(FakeViews.View(2, "end", hand: ["Snakeskin Veil", "Heroic Intervention"], battlefield: NeedleBoard(),
                actions: [FakeViews.Cast("a:sv", "Snakeskin Veil", "{G}"), FakeViews.Cast("a:hi", "Heroic Intervention", "{1}{G}")]), "s:9", "Atomize", ("p:2", "Pithing Needle")));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(2, "end", battlefield: NeedleBoard()), "Atomize", "answered",
                victims: [FakeViews.Victim("p:2", "Pithing Needle", hit: false)]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: NeedleBoard()), afterDisruption: true));
            await game.OverAsync("horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var engine = await XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(GrantProfiles), null, CancellationToken.None);
        var deck = (XmageDeck)await engine.PrepareAsync(GrantDeck, CancellationToken.None);

        var record = await PlayAsync(deck, Tier3(NeedleByTwo, Interactive((DisruptionKind.TargetedRemoval, 1)), horizon: 2));

        Assert.False(record.EngineError);
        Assert.Equal(["oppose", "a:hi", "continue", "continue"], Answers(fake));
        Assert.True(record.Success);
        Assert.Equal(["Heroic Intervention"], Assert.Single(record.Disruption!).AnsweredBy);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: targeted_removal casts Atomize on Pithing Needle; answered by Heroic Intervention.");
    }

    // ------------------------------------------------------------ interactive board wipe

    private static readonly GoalPredicate TalrandByTwo = new GoalPredicate.OnBattlefield("Talrand, Sky Summoner", 2);

    /// <summary>Talrand alone on the battlefield beside a tapped Island.</summary>
    private static JsonArray TalrandOnlyBoard() => [FakeViews.Permanent("p:1", "Island", tapped: true), FakeViews.Permanent("p:3", "Talrand, Sky Summoner")];

    /// <summary>A board wipe fires under the unchanged tier-3 wipe
    /// condition: the goal names a creature, and Talrand is on the
    /// battlefield. The oppose reply names Rout with empty targets; the
    /// opposed victims are the creatures it took, and the goal reads the
    /// after view.</summary>
    [Fact]
    public async Task An_interactive_board_wipe_opposes_rout_with_no_targets_at_the_end_step()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            // No creature yet: the goal names one, so the wipe waits.
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard())));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: TalrandOnlyBoard())));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(2, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)], commandZone: ["Talrand, Sky Summoner"]),
                "Rout", "hit", victims: [FakeViews.Victim("p:3", "Talrand, Sky Summoner", hit: true)], lost: ["Talrand, Sky Summoner"]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)], commandZone: ["Talrand, Sky Summoner"]), afterDisruption: true));
            await game.OverAsync("horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(TalrandByTwo, Interactive((DisruptionKind.BoardWipe, 1)), horizon: 2));

        Assert.False(record.EngineError);
        Assert.Equal(["continue", "oppose", "continue", "continue"], Answers(fake));
        var oppose = fake.Replies.ElementAt(1);
        Assert.Equal("Rout", (string?)oppose["card"]);
        Assert.Equal("[]", oppose["targets"]!.ToJsonString());
        Assert.False(record.Success);
        var outcome = Assert.Single(record.Disruption!);
        Assert.Equal(2, outcome.FiredTurn);
        Assert.Equal([new VictimHit("Talrand, Sky Summoner", true)], outcome.Hits);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: board_wipe casts Rout on Talrand, Sky Summoner; it hits.");
    }

    /// <summary>Removal and a wipe due on one turn run in profile order
    /// through the end-step chain: the wipe first, then Atomize on the
    /// settled view, then the after view.</summary>
    [Fact]
    public async Task Removal_and_a_wipe_on_one_turn_run_in_profile_order_through_the_end_step_chain()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: TalrandBoard())));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "end", battlefield: NeedleBoard(), commandZone: ["Talrand, Sky Summoner"]),
                "Rout", "hit", victims: [FakeViews.Victim("p:3", "Talrand, Sky Summoner", hit: true)], lost: ["Talrand, Sky Summoner"]));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)], graveyard: ["Pithing Needle"]),
                "Atomize", "hit", victims: [FakeViews.Victim("p:2", "Pithing Needle", hit: true)], lost: ["Pithing Needle"]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: [FakeViews.Permanent("p:1", "Island", tapped: true)], graveyard: ["Pithing Needle"]), afterDisruption: true));
            await game.OverAsync("horizon", 1);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Interactive((DisruptionKind.BoardWipe, 1), (DisruptionKind.TargetedRemoval, 1))));

        Assert.False(record.EngineError);
        Assert.Equal(["oppose", "oppose", "continue", "continue"], Answers(fake));
        Assert.Equal(["Rout", "Atomize"], fake.Replies.Take(2).Select(r => (string?)r["card"]));
        Assert.Equal("[]", fake.Replies.First()["targets"]!.ToJsonString());
        Assert.Equal("""["p:2"]""", fake.Replies.ElementAt(1)["targets"]!.ToJsonString()); // the settled view
        Assert.Equal([1, 1], record.Disruption!.Select(o => o.FiredTurn));
        Assert.Equal(["Talrand, Sky Summoner", "Pithing Needle"], record.Disruption!.Select(o => Assert.Single(o.Victims)));
    }

    /// <summary>The goal-aware pilot answers Rout on its goal creature with
    /// Heroic Intervention, a team indestructible grant. Snakeskin Veil is
    /// cheaper, but a target grant does not answer a wipe.</summary>
    [Fact]
    public async Task The_pilot_answers_rout_on_a_goal_creature_with_a_team_grant()
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: TalrandOnlyBoard())));
            await game.AskAsync(FakeViews.Respond(FakeViews.View(2, "end", hand: ["Snakeskin Veil", "Heroic Intervention"], battlefield: TalrandOnlyBoard(),
                actions: [FakeViews.Cast("a:sv", "Snakeskin Veil", "{G}"), FakeViews.Cast("a:hi", "Heroic Intervention", "{1}{G}")]), "s:9", "Rout", ("p:3", "Talrand, Sky Summoner")));
            await game.AskAsync(FakeViews.Opposed(FakeViews.View(2, "end", battlefield: TalrandOnlyBoard()), "Rout", "answered",
                victims: [FakeViews.Victim("p:3", "Talrand, Sky Summoner", hit: false)]));
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(2, "end", battlefield: TalrandOnlyBoard()), afterDisruption: true));
            await game.OverAsync("horizon", 2);
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var engine = await XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(GrantProfiles), null, CancellationToken.None);
        var deck = (XmageDeck)await engine.PrepareAsync(GrantDeck, CancellationToken.None);

        var record = await PlayAsync(deck, Tier3(TalrandByTwo, Interactive((DisruptionKind.BoardWipe, 2)), horizon: 2));

        Assert.False(record.EngineError);
        Assert.Equal(["oppose", "a:hi", "continue", "continue"], Answers(fake));
        Assert.True(record.Success);
        Assert.Equal(["Heroic Intervention"], Assert.Single(record.Disruption!).AnsweredBy);
        Assert.Contains(record.Log!, l => l.Message == "Disruption: board_wipe casts Rout on Talrand, Sky Summoner; answered by Heroic Intervention.");
    }

    public static TheoryData<string> RemovalViolations => new()
    {
        "after view before opposed",
        "game_over before opposed",
        "after view missing",
    };

    /// <summary>Each protocol violation of the end-step sequence is an
    /// engine error, with one outcome per event.</summary>
    [Theory]
    [MemberData(nameof(RemovalViolations))]
    public async Task An_end_step_protocol_violation_is_an_engine_error(string violation)
    {
        await using var fake = new FakeSidecar(async game =>
        {
            await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard())));
            switch (violation)
            {
                case "after view before opposed":
                    await game.AskAsync(FakeViews.EndOfTurn(FakeViews.View(1, "end", battlefield: NeedleBoard()), afterDisruption: true));
                    break;
                case "game_over before opposed":
                    await game.OverAsync("horizon", 1);
                    break;
                case "after view missing":
                    await game.AskAsync(FakeViews.Opposed(FakeViews.View(1, "end", battlefield: NeedleBoard()), "Atomize", "not_cast", reason: "no_target"));
                    await game.OverAsync("horizon", 1);
                    break;
            }

            await game.WaitClosedAsync();
        });
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Tier3(NeedleByOne, Interactive((DisruptionKind.TargetedRemoval, 1))));

        Assert.True(record.EngineError, violation);
        Assert.Single(record.Disruption!);
    }

    [Fact]
    public async Task A_dead_idle_connection_is_retried_once_on_a_new_one()
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne(ToTheHorizon));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);
        fake.DropConnections(); // a sidecar restart between requests

        var record = await PlayAsync(deck, Spec(NeedleByOne));

        Assert.False(record.EngineError);
        Assert.True(record.Success);
        Assert.Equal(2, fake.Connections);
    }

    [Fact]
    public async Task A_sidecar_gone_mid_batch_fails_the_batch_fast()
    {
        var fake = new FakeSidecar(NeedleOnTurnOne(ToTheHorizon));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (engine, deck) = await PrepareAsync(sidecar);
        await fake.DisposeAsync();

        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAsync<XmageUnavailableException>(() => MonteCarlo.RunAsync(
            engine, deck, new GoalAwarePilot(NeedleByOne), new GoalAwareMulligan(NeedleByOne),
            new MonteCarloOptions(Runs: 20, BaseSeed: 1, TurnHorizon: 2, Goal: NeedleByOne)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), clock.Elapsed.ToString());
    }

    [Fact]
    public async Task Prepare_loads_the_deck_once_and_reports_its_coverage()
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne(ToTheHorizon), unimplemented: ["Pithing Needle"]);
        await using var sidecar = new XmageSidecar(fake.Options());

        var (_, deck) = await PrepareAsync(sidecar);

        var load = Assert.Single(fake.Received, m => (string?)m["type"] == "load_deck");
        Assert.Equal("""["Talrand, Sky Summoner"]""", load["commanders"]!.ToJsonString());
        Assert.Equal("""[{"name":"Island","quantity":30},{"name":"Pithing Needle","quantity":4}]""", load["cards"]!.ToJsonString());
        Assert.Equal(FakeSidecar.DeckId, deck.DeckId);
        Assert.Equal(["Pithing Needle"], deck.Coverage.Unimplemented);
        Assert.Equal(["Talrand, Sky Summoner"], deck.Coverage.Unprofiled);
        Assert.Equal(CardFeatureProfile.None, deck.Views.Features(Deck[2])); // a blank card has no features, whatever its row
    }

    [Fact]
    public async Task The_engine_names_the_sidecar_version_and_its_threads()
    {
        await using var fake = new FakeSidecar(NeedleOnTurnOne(ToTheHorizon), threads: 3);
        await using var sidecar = new XmageSidecar(fake.Options());

        var (engine, _) = await PrepareAsync(sidecar);
        var (capped, _) = await PrepareAsync(sidecar, parallelism: 1);

        Assert.Equal("xmage", engine.Name);
        Assert.Equal(fake.Version, engine.Version);
        Assert.Equal(3, engine.Parallelism);
        Assert.Equal(1, capped.Parallelism);
        Assert.Equal(XmageEngine.SimplificationsFor(GameRules.Commander), engine.Simplifications);
    }

    [Fact]
    public async Task A_run_record_carries_the_game_digest()
    {
        var digest = new string('a', 64);
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 2, digest: digest));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Spec(NeedleByTwo));

        Assert.Equal(digest, record.Digest);
    }

    [Fact]
    public async Task An_engine_error_carries_no_digest()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("engine_error", 0, error: "boom"));
        await using var sidecar = new XmageSidecar(fake.Options());
        var (_, deck) = await PrepareAsync(sidecar);

        var record = await PlayAsync(deck, Spec(NeedleByTwo));

        Assert.True(record.EngineError);
        Assert.Null(record.Digest);
    }
}
