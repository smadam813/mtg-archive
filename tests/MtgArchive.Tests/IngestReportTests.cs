using System.Text.Json;
using MtgArchive.Data;
using MtgArchive.Ingest;

namespace MtgArchive.Tests;

/// <summary>
/// The legality list's C# names are format-neutral, but its wire and jsonb
/// shape stays <c>brawl_legality</c>. Pure: no Postgres, no host.
/// </summary>
public sealed class IngestReportTests
{
    [Fact]
    public void The_legality_list_serializes_as_brawl_legality()
    {
        using var document = BuildReport().ToDocument();
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("brawl_legality", out var list));
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Single(list.EnumerateArray());
        Assert.False(root.TryGetProperty("format_legality", out _));
    }

    [Fact]
    public void A_legality_entry_keeps_its_wire_fields()
    {
        using var document = BuildReport().ToDocument();
        var entry = document.RootElement.GetProperty("brawl_legality")[0];

        Assert.Equal(
            ["format", "legality_as_of", "legal", "banned", "not_legal", "status_changes"],
            entry.EnumerateObject().Select(p => p.Name).ToArray());

        var change = entry.GetProperty("status_changes")[0];
        Assert.Equal(
            ["oracle_id", "name", "from", "to"],
            change.EnumerateObject().Select(p => p.Name).ToArray());
    }

    private static IngestReport BuildReport()
    {
        var builder = new IngestReportBuilder();
        builder.FormatLegality.Add(new FormatLegalityReport(
            Format.Standard,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            Legal: 2,
            Banned: 1,
            NotLegal: 3,
            StatusChanges:
            [
                new LegalityStatusChange(Guid.NewGuid(), "Rotated Card", LegalityStatus.Legal, LegalityStatus.NotLegal),
            ]));
        return builder.Build();
    }
}
