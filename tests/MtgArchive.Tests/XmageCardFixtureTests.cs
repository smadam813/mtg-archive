using Json.Schema;

namespace MtgArchive.Tests;

/// <summary>
/// The XMage card-correctness fixtures (spec §6.2): every check of every
/// fixture passes on the real sidecar image, and every Oracle clause stays
/// asserted or marked not_modeled. An XMage card bug would make a floor
/// dishonest; these tests catch it.
/// </summary>
public sealed class XmageCardFixtureTests
{
    public static TheoryData<string, int> Checks()
    {
        var data = new TheoryData<string, int>();
        foreach (var (slug, fixture) in XmageCardFixtures.All())
        {
            for (var i = 0; i < fixture["checks"]!.AsArray().Count; i++)
            {
                data.Add(slug, i);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Checks))]
    public async Task Every_check_passes_on_the_sidecar(string slug, int check)
    {
        var fixture = XmageCardFixtures.All().Single(f => f.Slug == slug).Fixture;
        var body = fixture["checks"]![check]!.AsObject();

        var result = await XmageCardFixtures.RunAsync(await XmageSidecarContainer.OptionsAsync(), XmageCardFixtures.Request(body));

        var failures = XmageCardFixtures.Failures(body, result);
        Assert.True(failures.Count == 0, $"{slug} \"{(string)body["name"]!}\": {string.Join("; ", failures)}\nlog: {result.GetProperty("log")}");
    }

    [Fact]
    public void Every_fixture_passes_its_schema()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(XmageCardFixtures.Root, "xmage-card.schema.json")));
        foreach (var (slug, fixture) in XmageCardFixtures.All())
        {
            var result = schema.Evaluate(fixture, new EvaluationOptions { OutputFormat = OutputFormat.List });
            Assert.True(result.IsValid, $"{slug} fails xmage-card.schema.json");
        }
    }

    [Fact]
    public void Every_clause_is_asserted_or_marked_not_modeled()
    {
        foreach (var (slug, fixture) in XmageCardFixtures.All())
        {
            var ids = fixture["checks"]!.AsArray().SelectMany(c => c!["assertions"]!.AsArray()).Select(a => (string)a!["id"]!).ToList();
            Assert.True(ids.Count == ids.Distinct(StringComparer.Ordinal).Count(), $"{slug} repeats an assertion id");
            foreach (var clause in fixture["clause_map"]!.AsArray().Select(c => c!.AsObject()))
            {
                var mapped = clause["assertions"]?.AsArray().Select(n => (string)n!).ToList();
                Assert.True(mapped is { Count: > 0 } || clause["not_modeled"] is not null, $"{slug}: \"{(string)clause["clause"]!}\" is neither asserted nor marked not_modeled");
                Assert.All(mapped ?? [], id => Assert.Contains(id, ids));
            }
        }
    }
}
