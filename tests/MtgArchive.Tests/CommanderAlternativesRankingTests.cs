using MtgArchive.Api;
using MtgArchive.Cards;

namespace MtgArchive.Tests;

/// <summary>
/// Property suite over the commander-alternatives set arithmetic:
/// seeded random decks and candidate pools, checked against the
/// retention rule and the declared rank order.
/// </summary>
public sealed class CommanderAlternativesRankingTests
{
    private const int Trials = 300;

    [Fact]
    public void A_superset_candidate_loses_nothing_to_color_identity()
    {
        ForEachTrial(rng =>
        {
            var original = rng.Next(32);
            var deck = RandomDeck(rng, withinMask: original);
            var superset = original | rng.Next(32);
            var ranked = CommanderAlternatives.Evaluate(original, deck, Candidate(rng, superset));
            Assert.All(ranked.Lost, lost => Assert.NotEqual(LossReason.ColorIdentity, lost.Reason));
            Assert.Equal(deck.Count(m => m.FormatLoss is null), ranked.Retained.Count);
        });
    }

    [Fact]
    public void Lost_and_retained_partition_the_deck()
    {
        ForEachTrial(rng =>
        {
            var deck = RandomDeck(rng, withinMask: 31);
            var ranked = CommanderAlternatives.Evaluate(rng.Next(32), deck, Candidate(rng, rng.Next(32)));
            var retained = ranked.Retained.Select(m => m.OracleId).ToHashSet();
            var lost = ranked.Lost.Select(l => l.Member.OracleId).ToHashSet();
            Assert.Empty(retained.Intersect(lost));
            Assert.Equal(deck.Select(m => m.OracleId).ToHashSet(), retained.Union(lost).ToHashSet());
            Assert.Equal(deck.Sum(m => m.Quantity), ranked.RetainedCount + ranked.LostCount);
        });
    }

    [Fact]
    public void Every_loss_follows_the_rule_and_carries_its_citation()
    {
        ForEachTrial(rng =>
        {
            var deck = RandomDeck(rng, withinMask: 31);
            var candidate = Candidate(rng, rng.Next(32));
            var ranked = CommanderAlternatives.Evaluate(rng.Next(32), deck, candidate);
            foreach (var member in deck)
            {
                var escapes = (member.ColorIdentityMask & ~candidate.ColorIdentityMask) != 0;
                var lost = ranked.Lost.SingleOrDefault(l => l.Member == member);
                if (member.FormatLoss is { } loss)
                {
                    Assert.NotNull(lost);
                    Assert.Equal(loss.Reason, lost.Reason);
                    Assert.Equal(loss.Citation, lost.Citation);
                }
                else if (escapes)
                {
                    Assert.NotNull(lost);
                    Assert.Equal(LossReason.ColorIdentity, lost.Reason);
                    Assert.Equal(Citation.OfCard(member.OracleId, member.Name), lost.Citation);
                }
                else
                {
                    Assert.Null(lost);
                    Assert.Contains(member, ranked.Retained);
                }
            }
        });
    }

    [Fact]
    public void The_companion_follows_the_same_rule_and_never_counts()
    {
        ForEachTrial(rng =>
        {
            var deck = RandomDeck(rng, withinMask: 31);
            var candidate = Candidate(rng, rng.Next(32));
            var original = rng.Next(32);
            var companion = RandomMember(rng, 31, 1000) with { Quantity = 1 };
            var without = CommanderAlternatives.Evaluate(original, deck, candidate);
            var with = CommanderAlternatives.Evaluate(original, deck, candidate, companion);

            Assert.Null(without.Companion);
            Assert.Equal((without.RetainedCount, without.LostCount), (with.RetainedCount, with.LostCount));
            Assert.Equal(deck.Sum(m => m.Quantity), with.RetainedCount + with.LostCount);
            var outcome = Assert.IsType<CompanionOutcome>(with.Companion);
            Assert.Equal(companion, outcome.Member);
            var escapes = (companion.ColorIdentityMask & ~candidate.ColorIdentityMask) != 0;
            var (reason, citation) = companion.FormatLoss is { } loss ? (loss.Reason, loss.Citation)
                : escapes ? (LossReason.ColorIdentity, Citation.OfCard(companion.OracleId, companion.Name))
                : ((LossReason?)null, (Citation?)null);
            Assert.Equal(reason, outcome.Loss?.Reason);
            Assert.Equal(citation, outcome.Loss?.Citation);
        });
    }

    [Fact]
    public void A_wider_identity_retains_a_superset_of_a_narrower_one()
    {
        ForEachTrial(rng =>
        {
            var deck = RandomDeck(rng, withinMask: 31);
            var narrow = rng.Next(32);
            var wide = narrow | rng.Next(32);
            var original = rng.Next(32);
            var narrowRetained = CommanderAlternatives.Evaluate(original, deck, Candidate(rng, narrow)).Retained.Select(m => m.OracleId).ToHashSet();
            var wideRetained = CommanderAlternatives.Evaluate(original, deck, Candidate(rng, wide)).Retained.Select(m => m.OracleId).ToHashSet();
            Assert.True(narrowRetained.IsSubsetOf(wideRetained));
        });
    }

    [Fact]
    public void Rank_order_is_retained_count_then_closeness_then_popularity_rank()
    {
        ForEachTrial(rng =>
        {
            var original = rng.Next(32);
            var deck = RandomDeck(rng, withinMask: 31);
            var pool = Enumerable.Range(0, rng.Next(1, 40)).Select(_ => Candidate(rng, rng.Next(32))).ToList();
            var ranked = CommanderAlternatives.Rank(original, deck, pool, limit: pool.Count);
            Assert.Equal(pool.Count, ranked.Count);
            for (var i = 1; i < ranked.Count; i++)
            {
                var (before, after) = (ranked[i - 1], ranked[i]);
                Assert.True(before.RetainedCount >= after.RetainedCount, "retained count must not rise down the list");
                if (before.RetainedCount != after.RetainedCount)
                {
                    continue;
                }

                Assert.True(before.ColorIdentityDistance <= after.ColorIdentityDistance, "closeness must break retained ties");
                if (before.ColorIdentityDistance != after.ColorIdentityDistance)
                {
                    continue;
                }

                Assert.True(
                    before.Candidate.PopularityRank <= after.Candidate.PopularityRank,
                    "popularity_rank must break closeness ties");
            }
        });
    }

    [Fact]
    public void Closeness_is_the_symmetric_difference_and_the_original_identity_is_at_distance_zero()
    {
        ForEachTrial(rng =>
        {
            var a = rng.Next(32);
            var b = rng.Next(32);
            var expected = Enumerable.Range(0, 5).Count(bit => ((a >> bit) & 1) != ((b >> bit) & 1));
            Assert.Equal(expected, CommanderAlternatives.Distance(a, b));
            Assert.Equal(CommanderAlternatives.Distance(b, a), CommanderAlternatives.Distance(a, b));
            Assert.Equal(0, CommanderAlternatives.Distance(a, a));
        });
    }

    [Fact]
    public void Limit_keeps_the_head_of_the_full_order()
    {
        ForEachTrial(rng =>
        {
            var original = rng.Next(32);
            var deck = RandomDeck(rng, withinMask: 31);
            var pool = Enumerable.Range(0, rng.Next(1, 40)).Select(_ => Candidate(rng, rng.Next(32))).ToList();
            var limit = rng.Next(1, pool.Count + 5);
            var full = CommanderAlternatives.Rank(original, deck, pool, limit: pool.Count);
            var head = CommanderAlternatives.Rank(original, deck, pool, limit);
            Assert.Equal(
                full.Take(limit).Select(r => r.Candidate.OracleId),
                head.Select(r => r.Candidate.OracleId));
        });
    }

    private static void ForEachTrial(Action<Random> trial)
    {
        var rng = new Random(67);
        for (var i = 0; i < Trials; i++)
        {
            trial(rng);
        }
    }

    /// <summary>1–99 members, identities drawn inside <paramref name="withinMask"/>,
    /// quantities mostly 1 with the odd stack, about one in ten banned, and
    /// about one in ten not legal in the format.</summary>
    private static List<DeckMember> RandomDeck(Random rng, int withinMask)
        => [.. Enumerable.Range(0, rng.Next(1, 100)).Select(i => RandomMember(rng, withinMask, i))];

    private static DeckMember RandomMember(Random rng, int withinMask, int i)
    {
        var id = Guid.NewGuid();
        var loss = rng.Next(10) switch
        {
            0 => new FormatLoss(LossReason.Banned, new Citation(CitationType.BanEvent, i.ToString(), $"Member {i} banned")),
            1 => new FormatLoss(LossReason.NotLegal, new Citation(CitationType.Legality, id.ToString(), "brawl not_legal as of 2026-10-03")),
            _ => null,
        };
        return new DeckMember(id, $"Member {i}", rng.Next(4) == 0 ? rng.Next(2, 12) : 1, rng.Next(32) & withinMask, loss);
    }

    // The popularity rank and edhrec_rank are drawn apart, so the order
    // shows which one breaks closeness ties.
    private static CommanderCandidate Candidate(Random rng, int mask)
        => new(
            Guid.NewGuid(),
            $"Candidate {rng.Next(1000)}",
            [],
            mask,
            PopularityRank: rng.Next(1, 50_000),
            EdhrecRank: rng.Next(3) == 0 ? null : rng.Next(1, 5000),
            Available: null);
}
