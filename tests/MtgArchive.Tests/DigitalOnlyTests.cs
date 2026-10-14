using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The derived <c>digital_only</c> flag. Scryfall
/// <c>digital</c> describes the one printing that Oracle Cards stores, not
/// the card. A paper card with an MTGO reprint as its stored printing reads
/// <c>digital: true</c>. So the flag is true only when Scryfall
/// <c>digital</c> is true and the raw Scryfall <c>legalities.commander</c>
/// value is <c>not_legal</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DigitalOnlyTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(true, "not_legal", true)]
    [InlineData(true, "legal", false)]
    [InlineData(true, "banned", false)]
    [InlineData(false, "not_legal", false)]
    [InlineData(false, "legal", false)]
    [InlineData(null, "not_legal", false)]
    [InlineData(true, null, false)]
    [InlineData(null, null, false)]
    public void Digital_only_is_a_digital_printing_that_commander_does_not_allow(
        bool? digital, string? commanderLegality, bool expected)
        => Assert.Equal(expected, CardDerivations.DigitalOnly(digital, commanderLegality));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void The_reader_keeps_a_boolean_digital(bool? digital)
        => Assert.Equal(digital, ReadOne(CardLine(Guid.NewGuid(), "Probe", digital: digital)).Digital);

    [Fact]
    public void The_reader_reads_a_string_digital_as_null()
    {
        var line = CardLine(Guid.NewGuid(), "Probe", digital: true).Replace("\"digital\":true", "\"digital\":\"true\"");
        Assert.Contains("\"digital\":\"true\"", line);
        Assert.Null(ReadOne(line).Digital);
    }

    [Fact]
    public void The_reader_reads_a_missing_digital_as_null()
    {
        var line = CardLine(Guid.NewGuid(), "Probe").Replace("\"digital\":null,", "");
        Assert.DoesNotContain("digital", line);
        Assert.Null(ReadOne(line).Digital);
    }

    [Fact]
    public async Task Card_detail_serves_digital_only_from_the_derived_rule()
    {
        // Perennial Gravewarden is an Alchemy card. Demonic Tutor is paper.
        // Sphere of Resistance is paper, but Oracle Cards stores its
        // MTGO-only vma printing, so Scryfall reads it digital.
        using var app = await IngestAsync(
            CardLine(Guid.NewGuid(), "Perennial Gravewarden", commanderLegality: "not_legal", digital: true,
                brawl: new BrawlKeys("legal", "legal", "not_legal")),
            CardLine(Guid.NewGuid(), "Demonic Tutor", commanderLegality: "banned", digital: false),
            CardLine(Guid.NewGuid(), "Sphere of Resistance", commanderLegality: "legal", digital: true));
        using var client = app.CreateClient();

        Assert.True(await DigitalOnlyAsync(client, "Perennial Gravewarden"));
        Assert.False(await DigitalOnlyAsync(client, "Demonic Tutor"));
        Assert.False(await DigitalOnlyAsync(client, "Sphere of Resistance"));
    }

    [Fact]
    public async Task A_rerun_fills_digital_only_without_counting_the_card_as_changed()
    {
        var source = new FakeBulkSource();
        using var app = await HostAsync(source);
        using var client = app.CreateClient();
        var id = Guid.NewGuid();

        source.OracleCards = Lines(CardLine(id, "Perennial Gravewarden", commanderLegality: "not_legal"));
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        Assert.False(await DigitalOnlyAsync(client, "Perennial Gravewarden"));

        source.OracleCards = Lines(CardLine(id, "Perennial Gravewarden", commanderLegality: "not_legal", digital: true));
        var report = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        Assert.True(await DigitalOnlyAsync(client, "Perennial Gravewarden"));
        Assert.Equal(0, report.GetProperty("report").GetProperty("changed_cards").GetInt32());
    }

    private static async Task<bool> DigitalOnlyAsync(HttpClient client, string name)
    {
        var response = await client.GetAsync($"/cards?name={Uri.EscapeDataString(name)}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        return body.RootElement.GetProperty("digital_only").GetBoolean();
    }

    private async Task<ArchiveApp> IngestAsync(params string[] lines)
    {
        var source = new FakeBulkSource();
        var app = await HostAsync(source);
        using var client = app.CreateClient();
        source.OracleCards = Lines(lines);
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        return app;
    }

    private async Task<ArchiveApp> HostAsync(FakeBulkSource source)
        => new(
            autoMigrate: true,
            await postgres.CreateDatabaseAsync($"digital_{Guid.NewGuid():N}"),
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            });
}
