using MtgArchive.Data;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// XmageEngine against the real sidecar image. The decks are test decks,
/// so every name is a real card, and their profiles are the ones the test
/// deck files froze. Each test builds its own
/// <see cref="XmageSidecar"/> pool on the shared container.
/// </summary>
public sealed class XmageLiveTests
{
    private static readonly TestDeck Dq2 = TestDecks.Load("dq2-needle-goal-aware");

    [Fact]
    public async Task The_sidecar_names_its_version_and_threads()
    {
        await using var sidecar = await SidecarAsync();

        var engine = await EngineAsync(sidecar, Dq2);

        Assert.Equal("xmage", engine.Name);
        Assert.Matches(@"^xmage-1\.4\.62V1\+[0-9a-f]{8}$", engine.Version);
        Assert.Equal(XmageSidecarContainer.Threads, engine.Parallelism);
    }

    /// <summary>Phase 2, Review Focus 4: the same request twice gives the same result, at parallelism 1 and 4.</summary>
    [Fact]
    public async Task A_batch_gives_the_same_result_at_parallelism_one_and_four_and_again()
    {
        await using var sidecar = await SidecarAsync();
        var options = new MonteCarloOptions(Runs: 24, BaseSeed: 20260927, TurnHorizon: 3, Goal: new GoalPredicate.OnBattlefield("Pithing Needle", 3));

        var serial = await BatchAsync(sidecar, Dq2, options, parallelism: 1);
        var parallel = await BatchAsync(sidecar, Dq2, options, parallelism: 4);
        var again = await BatchAsync(sidecar, Dq2, options, parallelism: 4);

        Assert.Equal(serial, parallel);
        Assert.Equal(parallel, again);
    }

    /// <summary>Phase 2, Review Focus 2: two batches at once share one pool and never mix games.</summary>
    [Fact]
    public async Task Two_batches_at_once_share_the_sidecar_and_match_their_runs_alone()
    {
        await using var sidecar = await SidecarAsync();
        var staples = TestDecks.Load("staples-needle-goal-aware");
        var needle = new GoalPredicate.OnBattlefield("Pithing Needle", 3);
        var dq2Options = new MonteCarloOptions(Runs: 16, BaseSeed: 7, TurnHorizon: 3, Goal: needle);
        var staplesOptions = new MonteCarloOptions(Runs: 16, BaseSeed: 8, TurnHorizon: 3, Goal: needle);

        var dq2Alone = await BatchAsync(sidecar, Dq2, dq2Options);
        var staplesAlone = await BatchAsync(sidecar, staples, staplesOptions);
        var together = await Task.WhenAll(BatchAsync(sidecar, Dq2, dq2Options), BatchAsync(sidecar, staples, staplesOptions));

        Assert.Equal(dq2Alone, together[0]);
        Assert.Equal(staplesAlone, together[1]);
    }

    /// <summary>Spec §4.4: early stop changes no run record.</summary>
    [Fact]
    public async Task Early_stop_keeps_every_run_record()
    {
        await using var sidecar = await SidecarAsync();
        var engine = await EngineAsync(sidecar, Dq2);
        var deck = await engine.PrepareAsync(Dq2.Deck, CancellationToken.None);
        var goal = new GoalPredicate.OnBattlefield("Pithing Needle", 2);

        for (var index = 0; index < 12; index++)
        {
            var seed = RunSeeds.Derive(5, index);
            var stopped = await deck.PlayAsync(new RunSpec(index, seed, 4, goal, null, EarlyStop: true, CaptureLog: false), new GoalAwarePilot(goal), new GoalAwareMulligan(goal), CancellationToken.None);
            var full = await deck.PlayAsync(new RunSpec(index, seed, 4, goal, null, EarlyStop: false, CaptureLog: false), new GoalAwarePilot(goal), new GoalAwareMulligan(goal), CancellationToken.None);

            Assert.Equal((full.Success, full.MetTurn, false), (stopped.Success, stopped.MetTurn, stopped.EngineError));
        }
    }

    /// <summary>Phase 2, Review Focus 1: a card XMage lacks is a blank card that keeps its slot and never casts.</summary>
    [Fact]
    public async Task A_card_xmage_lacks_is_a_blank_card_that_never_casts()
    {
        await using var sidecar = await SidecarAsync();
        var blank = TestDeckCards.Spell("No Such Card In Xmage", "{1}", "Artifact", quantity: 20);
        IReadOnlyList<DeckCard> deck = [.. Dq2.Deck, blank];
        var engine = await EngineAsync(sidecar, Dq2);
        var prepared = (XmageDeck)await engine.PrepareAsync(deck, CancellationToken.None);
        var goal = new GoalPredicate.OnBattlefield("Pithing Needle", 4);
        var pilot = new XmageViewRecorder(new GoalAwarePilot(goal));

        var result = await MonteCarlo.RunAsync(engine, prepared, pilot, new GoalAwareMulligan(goal), new MonteCarloOptions(Runs: 12, BaseSeed: 3, TurnHorizon: 4, Goal: goal));

        Assert.Equal(["No Such Card In Xmage"], prepared.Coverage.Unimplemented);
        Assert.Equal(0, result.EngineErrors);
        Assert.Contains(pilot.Views, v => v.Hand.Any(c => c.Name == blank.Name));
        Assert.DoesNotContain(pilot.Views.SelectMany(v => v.Actions), a => a.Face.Name == blank.Name);
        Assert.Equal(CardFeatureProfile.None, prepared.Views.Features(blank));
    }

    /// <summary>Phase 2, Review Focus 5: a back face on the wire maps to the back-face card.</summary>
    [Fact]
    public async Task Back_faces_on_the_wire_map_to_the_back_face()
    {
        await using var sidecar = await SidecarAsync();
        var mdfc = TestDecks.Load("mdfc-bridge");
        var engine = await EngineAsync(sidecar, mdfc);
        var prepared = (XmageDeck)await engine.PrepareAsync(mdfc.Deck, CancellationToken.None);
        var pilot = new XmageViewRecorder(new GoalAwarePilot(mdfc.Goal));
        string[] backFaces = ["The Prismatic Bridge", "Shatterskull, the Hammer Pass", "Grimclimb Pathway"];

        var result = await MonteCarlo.RunAsync(engine, prepared, pilot, new GoalAwareMulligan(mdfc.Goal), new MonteCarloOptions(Runs: 40, BaseSeed: 11, TurnHorizon: 6, Goal: mdfc.Goal));

        var backs = pilot.Views.SelectMany(v => v.Battlefield).Select(p => p.Card).Where(c => backFaces.Contains(c.Name)).ToList();
        Assert.NotEmpty(backs);
        Assert.All(backs, c => Assert.NotNull(c.WholeCard));
        Assert.All(pilot.Views.SelectMany(v => v.Actions).Where(a => a.Face.WholeCard is not null), a => Assert.Contains(a.Face.Name, backFaces));
        Assert.Equal(0, result.EngineErrors);
        Assert.Empty(prepared.Views.OutsideNames);
    }

    /// <summary>
    /// Final review I1: a combo win scores the winning cast. Eight Islands and
    /// Thassa's Oracle, kept at seven, put every card in hand by turn 2, so two
    /// Islands cast the Oracle into an empty library and its trigger wins with
    /// no pick. The trigger is on the stack when the Oracle resolves, so only
    /// game_over's final view holds the cast.
    /// </summary>
    [Fact]
    public async Task A_combo_win_scores_the_winning_cast()
    {
        await using var sidecar = await SidecarAsync();
        IReadOnlyList<DeckCard> deck =
        [
            TestDeckCards.Commander("Talrand, Sky Summoner", "{2}{U}{U}"),
            TestDeckCards.Land("Island", "U", quantity: 8),
            TestDeckCards.Spell("Thassa's Oracle", "{U}{U}", "Creature"),
        ];
        var profiles = new DeckFeatureProfiles(
            new Dictionary<string, CardFeatureProfile>
            {
                ["Island"] = new(true, false, ["{U}"], [], null, null, true),
                ["Thassa's Oracle"] = new(false, false, [], [], null, null, true),
            },
            ["Talrand, Sky Summoner"]);
        var engine = await XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(profiles), null, CancellationToken.None);
        var prepared = await engine.PrepareAsync(deck, CancellationToken.None);

        foreach (GoalPredicate goal in (GoalPredicate[])[new GoalPredicate.Cast("Thassa's Oracle", 2), new GoalPredicate.OnBattlefield("Thassa's Oracle", 2)])
        {
            var result = await MonteCarlo.RunAsync(engine, prepared, new GoalAwarePilot(goal), new ScriptedViewMulligan { Keep = _ => true }, new MonteCarloOptions(Runs: 8, BaseSeed: 7, TurnHorizon: 5, Goal: goal));

            Assert.Equal(0, result.EngineErrors);
            Assert.Equal(result.Runs, result.Successes);
        }
    }

    /// <summary>
    /// Phase 3A, Review Focus 5 and spec §7: the done-query 2 tier-3 test deck
    /// (Needle by turn 3, counterspell turn 3, targeted removal turn 4)
    /// runs paired on the real image with no engine error, some events
    /// fire, and the same batch gives the same result at parallelism 1 and 4.
    /// </summary>
    [Fact]
    public async Task A_tier_three_batch_runs_on_the_sidecar_and_replays_the_same()
    {
        await using var sidecar = await SidecarAsync();
        var disrupted = TestDecks.Load("dq2-needle-disrupted");
        var options = new MonteCarloOptions(40, disrupted.Options.BaseSeed, disrupted.Options.TurnHorizon, disrupted.Goal, disrupted.Options.Disruption);

        async Task<(string Snapshot, PairedResult Paired)> RunAsync(int parallelism)
        {
            var engine = await EngineAsync(sidecar, disrupted, parallelism);
            var deck = await engine.PrepareAsync(disrupted.Deck, CancellationToken.None);
            var paired = await MonteCarlo.RunPairedAsync(engine, deck, new GoalAwarePilot(disrupted.Goal), new GoalAwareMulligan(disrupted.Goal), options);
            var snapshot = GoldenSnapshot.Of(paired.Baseline) with { Disrupted = GoldenSnapshot.Of(paired.Disrupted) };
            return (TestDecks.Serialize(snapshot), paired);
        }

        var serial = await RunAsync(1);
        var parallel = await RunAsync(4);

        Assert.Equal(serial.Snapshot, parallel.Snapshot);
        Assert.Equal(0, serial.Paired.Baseline.EngineErrors);
        Assert.Equal(0, serial.Paired.Disrupted.EngineErrors);
        Assert.Contains(serial.Paired.Events, e => e.FiredRuns > 0);
        Assert.All(serial.Paired.Disrupted.Exemplars, e => Assert.NotNull(e.Events));
    }

    /// <summary>Every name, face and mana string of real games reads cleanly.</summary>
    [Fact]
    public async Task Every_view_of_a_real_deck_reads_cleanly()
    {
        await using var sidecar = await SidecarAsync();
        var engine = await EngineAsync(sidecar, Dq2);
        var prepared = (XmageDeck)await engine.PrepareAsync(Dq2.Deck, CancellationToken.None);
        var goal = new GoalPredicate.OnBattlefield("Pithing Needle", 6);
        var pilot = new XmageViewRecorder(new GoalAwarePilot(goal));

        var result = await MonteCarlo.RunAsync(engine, prepared, pilot, new GoalAwareMulligan(goal), new MonteCarloOptions(Runs: 24, BaseSeed: 12, TurnHorizon: 6, Goal: goal));

        Assert.Equal(0, result.EngineErrors);
        Assert.Empty(prepared.Views.UnreadMana);
        Assert.All(prepared.Views.OutsideNames, name => Assert.Equal("Treasure Token", name)); // Deadly Dispute's token, named for its subtype plus "Token" (CR 111.4)
        Assert.Contains(pilot.Views, v => v.Mana.Sources.Count > 0);
        Assert.True(result.Successes > 0);
        Assert.All(result.Exemplars, e => Assert.NotEmpty(e.Log));
    }

    /// <summary>Carried-forward item 2: the pilot, not XMage, answers the order, put-back and shuffle picks of the done-query 2 cantrips.</summary>
    [Fact]
    public async Task The_pilot_answers_the_library_order_picks()
    {
        await using var sidecar = await SidecarAsync();
        var engine = await EngineAsync(sidecar, Dq2);
        var prepared = await engine.PrepareAsync(Dq2.Deck, CancellationToken.None);
        var goal = new GoalPredicate.OnBattlefield("Pithing Needle", 6);
        var pilot = new XmageViewRecorder(new GoalAwarePilot(goal));

        var result = await MonteCarlo.RunAsync(engine, prepared, pilot, new GoalAwareMulligan(goal), new MonteCarloOptions(Runs: 60, BaseSeed: 13, TurnHorizon: 6, Goal: goal));

        Assert.Equal(0, result.EngineErrors);
        var purposes = pilot.Picks.Select(p => p.Purpose).ToHashSet();
        Assert.Contains(PickPurpose.OrderTop, purposes);
        Assert.Contains(PickPurpose.PutBack, purposes);
        Assert.Contains(PickPurpose.Shuffle, purposes);
        Assert.All(pilot.Picks.Where(p => p.Purpose == PickPurpose.Shuffle), p => Assert.NotNull(p.Seen));
        Assert.Contains(pilot.Picks, p => p.Purpose == PickPurpose.Shuffle && p.Seen is { Count: > 0 });
    }

    /// <summary>
    /// The shim plays the game rules start_game carries (ADR 0012): on the
    /// play at 25 life, turn 1 ends with the seven-card hand at 25 life; with
    /// the Commander rules on the same seed it ends with the turn-1 draw at 40.
    /// </summary>
    [Fact]
    public async Task A_game_on_the_play_starts_at_its_start_life_and_skips_the_turn_one_draw()
    {
        var onThePlay = new GameRules(StartLife: 25, CommanderDamage: false, StartingPlayerDraws: false, FreeMulligans: 1);

        var play = await FirstEndOfTurnAsync(onThePlay);
        var draw = await FirstEndOfTurnAsync(GameRules.Commander);

        Assert.Equal((1, 25, 7), (play.Turn, play.Life, play.Hand.Count));
        Assert.Equal((1, 40, 8), (draw.Turn, draw.Life, draw.Hand.Count));
        Assert.Equal(draw.LibraryCount + 1, play.LibraryCount);
    }

    /// <summary>
    /// Tier 1 and tier 2 count the same cards seen in every format (ADR
    /// 0012). Each format's game plays on the wire with every keep taken and
    /// every action passed, so no card leaves the library but by a draw:
    /// cards seen by turn T are the 7-card hand plus the library's loss since
    /// the keep. The keep's view carries the start life that the mulligan's
    /// hand payability reads.
    /// </summary>
    [Theory]
    [InlineData(Format.Commander)]
    [InlineData(Format.Brawl)]
    [InlineData(Format.CompetitiveBrawl)]
    [InlineData(Format.StandardBrawl)]
    public async Task Tier_one_and_tier_two_count_the_same_cards_seen_and_the_mulligan_sees_the_start_life(Format format)
    {
        var rules = GameRules.For(format);

        var (keep, ends) = await PlayPassingAsync(rules, horizon: 3);

        Assert.Equal(rules.StartLife, keep.Life);
        Assert.Equal(CardsSeen.Opener, keep.Hand.Count);
        Assert.Equal([1, 2, 3], ends.Select(e => e.Turn));
        Assert.All(ends, end => Assert.Equal(
            CardsSeen.ByTurn(end.Turn, rules),
            CardsSeen.Opener + keep.LibraryCount - end.LibraryCount));
    }

    /// <summary>Plays Dq2 on the raw wire with every keep taken and every action passed; returns turn 1's end view.</summary>
    private static async Task<WireView> FirstEndOfTurnAsync(GameRules rules)
        => (await PlayPassingAsync(rules, horizon: 1)).Ends[0];

    /// <summary>Plays Dq2 on the raw wire with every keep taken and every
    /// action passed; returns the first keep decision's view and each
    /// turn's first end-of-turn view.</summary>
    private static async Task<(WireView Keep, List<WireView> Ends)> PlayPassingAsync(GameRules rules, int horizon)
    {
        var (connection, _) = await XmageConnection.OpenAsync(await XmageSidecarContainer.OptionsAsync(), CancellationToken.None);
        await using var _connection = connection;
        await connection.SendAsync(XmageRequests.LoadDeck(Dq2.Deck), CancellationToken.None);
        var deckId = (await connection.ReceiveAsync(CancellationToken.None)).As<WireDeck>().DeckId;
        await connection.SendAsync(XmageRequests.StartGame(deckId, 20261004, horizon, rules, watchCasts: false, log: false), CancellationToken.None);
        WireView? keep = null;
        var ends = new List<WireView>();
        while (true)
        {
            var message = await connection.ReceiveAsync(CancellationToken.None);
            if (message.Type == "game_over")
            {
                return ends.Count == horizon && keep is not null
                    ? (keep, ends)
                    : throw new InvalidOperationException($"The game ended before turn {horizon}'s end: {message.Json}");
            }

            if (message.Type == "event")
            {
                var e = message.As<WireEvent>();
                if (e.Kind == "end_of_turn" && ends.All(v => v.Turn != e.View.Turn))
                {
                    ends.Add(e.View);
                }

                await connection.SendAsync(XmageRequests.Do(e.Seq, "continue"), CancellationToken.None);
                continue;
            }

            var decide = message.As<WireDecide>();
            if (decide.Kind == "keep")
            {
                keep ??= decide.View;
            }

            await connection.SendAsync(decide.Kind switch
            {
                "keep" => XmageRequests.Keep(decide.Seq, true),
                "bottom" or "discard" => XmageRequests.Cards(decide.Seq, decide.View.Hand.Take(decide.Count!.Value)),
                "pick" => XmageRequests.Option(decide.Seq, decide.Pick!.Options[0].Id),
                _ => XmageRequests.Action(decide.Seq, null),
            }, CancellationToken.None);
        }
    }

    private static async Task<XmageSidecar> SidecarAsync() => new(await XmageSidecarContainer.OptionsAsync());

    private static Task<XmageEngine> EngineAsync(XmageSidecar sidecar, TestDeck deck, int? parallelism = null)
        => XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(deck.Profiles), parallelism, CancellationToken.None);

    /// <summary>One batch as the golden snapshot text: every field the envelope shows, and each exemplar's log.</summary>
    private static async Task<string> BatchAsync(XmageSidecar sidecar, TestDeck deck, MonteCarloOptions options, int? parallelism = null)
    {
        var engine = await EngineAsync(sidecar, deck, parallelism);
        var prepared = await engine.PrepareAsync(deck.Deck, CancellationToken.None);
        var result = await MonteCarlo.RunAsync(engine, prepared, new GoalAwarePilot(options.Goal!), new GoalAwareMulligan(options.Goal!), options);
        Assert.Equal(0, result.EngineErrors);
        return TestDecks.Serialize(GoldenSnapshot.Of(result));
    }
}
