using System.Text;
using MtgArchive.CollectionImport;

namespace MtgArchive.Tests;

/// <summary>
/// Collection-import parsing: the ManaBox CSV parser over the
/// three attested header variants and the plain-text decklist parser, both
/// producing the shared <see cref="ImportRow"/> model. Fixtures under
/// <c>fixtures/collection-import/</c> are reconstructed from the verbatim
/// sample rows in <c>docs/research/manabox-export-schema.md</c>; the
/// 17-column fixture is deliberately CRLF and the others LF.
/// </summary>
public sealed class CollectionImportParserTests
{
    [Fact]
    public void Fifteen_column_core_export_parses_by_header()
    {
        var rows = ParseFixture("manabox-15-column.csv");

        Assert.Equal(7, rows.Count);
        Assert.All(rows, r => Assert.Equal(15, r.Provenance.Count));

        // The quoted-name sample row: RFC 4180 unquoting, blank cells legal
        // in ManaBox ID / price / Misprint / Altered / currency.
        var isperia = rows[0];
        Assert.Equal("Isperia, Supreme Judge", isperia.Name);
        Assert.Equal(1, isperia.Quantity);
        Assert.Equal(2, isperia.LineNumber);
        Assert.Equal("Isperia, Supreme Judge", isperia.Provenance["Name"]);
        Assert.Equal("GK2", isperia.Provenance["Set code"]);
        Assert.Equal("foil", isperia.Provenance["Foil"]);
        Assert.Equal("", isperia.Provenance["ManaBox ID"]);
        Assert.Equal("", isperia.Provenance["Purchase price"]);
        Assert.Equal("", isperia.Provenance["Misprint"]);
        Assert.Equal("", isperia.Provenance["Altered"]);
        Assert.Equal("", isperia.Provenance["Purchase price currency"]);

        // Collector number is a string, never numeric.
        Assert.Equal("DDC-49", rows[1].Provenance["Collector number"]);

        // Full multi-face name, kept whole for exact-match resolution.
        Assert.Equal("Fire // Ice", rows[2].Name);

        // Non-ASCII kept verbatim (already NFC in the export).
        Assert.Equal("Bespoke Bō", rows[3].Name);

        // A token row with a blank Condition; ManaBox does export tokens.
        Assert.Equal("Clue", rows[4].Name);
        Assert.Equal(3, rows[4].Quantity);
        Assert.Equal("", rows[4].Provenance["Condition"]);

        // ManaBox's Chinese codes differ from Scryfall's; provenance only.
        Assert.Equal("zh_CN", rows[5].Provenance["Language"]);

        // A misscan-style garbled name parses like any other; resolution,
        // not parsing, quarantines it (the collection e2e fixture row).
        Assert.Equal("Lighming Bolt", rows[6].Name);
    }

    [Fact]
    public void Seventeen_column_export_parses_with_crlf_and_binder_columns()
    {
        var bytes = File.ReadAllBytes(FixturePath("manabox-17-column.csv"));
        Assert.Contains("\r\n", Encoding.UTF8.GetString(bytes));

        var rows = ParseFixture("manabox-17-column.csv");

        Assert.Equal(2, rows.Count);
        var matron = rows[0];
        Assert.Equal("Abbey Matron", matron.Name);
        Assert.Equal(1, matron.Quantity);
        Assert.Equal("White Creatures", matron.Provenance["Binder Name"]);
        Assert.Equal("binder", matron.Provenance["Binder Type"]);
        Assert.Equal("2a", matron.Provenance["Collector number"]);
        Assert.Equal("39299", matron.Provenance["ManaBox ID"]);
        Assert.Equal("deck", rows[1].Provenance["Binder Type"]);
        Assert.Equal(3, rows[1].Quantity);
    }

    [Fact]
    public void Eighteen_column_export_parses_with_added_timestamps()
    {
        var rows = ParseFixture("manabox-18-column.csv");

        Assert.Equal(2, rows.Count);
        Assert.Equal("2026-07-11T18:44:33.509Z", rows[0].Provenance["Added"]);

        // Quoted multi-face name with an embedded comma, from the 18-column
        // magic-strategist export.
        Assert.Equal("Abigale, Poet Laureate // Heroic Stanza", rows[1].Name);
        Assert.Equal("list", rows[1].Provenance["Binder Type"]);
    }

    // Column mapping is header-driven, never positional: the same columns in
    // a scrambled order still land on the right fields.
    [Fact]
    public void Column_order_does_not_matter()
    {
        var rows = ManaBoxCsvParser.Parse(new StringReader(
            "Quantity,Language,Name\n4,en,Demonic Tutor\n"));

        var row = Assert.Single(rows);
        Assert.Equal("Demonic Tutor", row.Name);
        Assert.Equal(4, row.Quantity);
        Assert.Equal("en", row.Provenance["Language"]);
    }

    // The corpus is NFC; a decomposed export row must match it anyway.
    [Fact]
    public void Csv_names_are_nfc_normalized()
    {
        var decomposed = "Bespoke Bo\u0304";
        var rows = ManaBoxCsvParser.Parse(new StringReader(
            $"Name,Quantity\n{decomposed},1\n"));

        Assert.Equal("Bespoke Bō", rows[0].Name);
        // Provenance keeps the cell verbatim, un-normalized.
        Assert.Equal(decomposed, rows[0].Provenance["Name"]);
    }

    // Unknown values in provenance-only enum columns are never fatal — the
    // parser interprets nothing but Name and Quantity.
    [Fact]
    public void Unknown_provenance_values_parse_untouched()
    {
        var rows = ManaBoxCsvParser.Parse(new StringReader(
            "Name,Foil,Condition,Rarity,Quantity\nAbrade,glittery,slightly_chewed,bonus,1\n"));

        var row = Assert.Single(rows);
        Assert.Equal("glittery", row.Provenance["Foil"]);
        Assert.Equal("slightly_chewed", row.Provenance["Condition"]);
        Assert.Equal("bonus", row.Provenance["Rarity"]);
    }

    [Fact]
    public void Quoted_fields_may_contain_doubled_quotes_and_newlines()
    {
        var rows = ManaBoxCsvParser.Parse(new StringReader(
            "Name,Set name,Quantity\n\"Ach! Hans, Run!\",\"Unhinged \"\"line\nbreak\"\"\",1\n"));

        var row = Assert.Single(rows);
        Assert.Equal("Ach! Hans, Run!", row.Name);
        Assert.Equal("Unhinged \"line\nbreak\"", row.Provenance["Set name"]);
    }

    [Theory]
    [InlineData("Set code,Quantity\nGK2,1\n", "Name")]
    [InlineData("Name,Set code\nAbrade,VOW\n", "Quantity")]
    public void Missing_required_header_column_is_a_format_error_naming_the_line(string csv, string column)
    {
        var error = Assert.Throws<FormatException>(() => ManaBoxCsvParser.Parse(new StringReader(csv)));
        Assert.Contains(column, error.Message);
        Assert.Contains("Line 1", error.Message);
    }

    // A BOM must be consumed before tokenizing: stripped afterwards it would
    // already have defeated quote recognition on a quoted first header cell.
    [Fact]
    public void Bom_before_a_quoted_first_header_cell_is_stripped()
    {
        var rows = ManaBoxCsvParser.Parse(new StringReader(
            "\uFEFF\"Name\",Quantity\n\"Isperia, Supreme Judge\",1\n"));

        var row = Assert.Single(rows);
        Assert.Equal("Isperia, Supreme Judge", row.Name);
    }

    [Theory]
    [InlineData("Name,Quantity\nAbrade,two\n")]
    [InlineData("Name,Quantity\nAbrade,0\n")]
    [InlineData("Name,Quantity\nAbrade,-1\n")]
    public void Non_positive_quantity_is_a_format_error_naming_the_line(string csv)
    {
        var error = Assert.Throws<FormatException>(() => ManaBoxCsvParser.Parse(new StringReader(csv)));
        Assert.Contains("Line 2", error.Message);
    }

    [Fact]
    public void Ragged_row_is_a_format_error_naming_the_line()
    {
        var error = Assert.Throws<FormatException>(() => ManaBoxCsvParser.Parse(new StringReader(
            "Name,Set code,Quantity\nAbrade,VOW,3\nDemonic Tutor,1\n")));
        Assert.Contains("Line 3", error.Message);
    }

    [Fact]
    public void Decklist_fixture_parses_quantity_and_exact_name_per_line()
    {
        using var reader = new StreamReader(FixturePath("decklist.txt"));
        var rows = DecklistParser.Parse(reader);

        Assert.Equal(5, rows.Count);
        Assert.Equal("Pithing Needle", rows[0].Name);
        Assert.Equal(1, rows[0].Quantity);
        Assert.Equal(1, rows[0].LineNumber);
        Assert.Equal(2, rows[1].Quantity);

        // A full multi-face name line stays whole.
        Assert.Equal("Fire // Ice", rows[2].Name);

        // Multi-digit quantity; name is everything after the first space.
        Assert.Equal("Island", rows[3].Name);
        Assert.Equal(10, rows[3].Quantity);
        Assert.Equal("Bespoke Bō", rows[4].Name);

        // Plain-text rows carry no provenance columns at all.
        Assert.All(rows, r => Assert.Empty(r.Provenance));
    }

    [Fact]
    public void Decklist_names_are_nfc_normalized_and_blank_lines_skipped()
    {
        var rows = DecklistParser.Parse(new StringReader("\n2 Bespoke Bo\u0304\n\n"));

        var row = Assert.Single(rows);
        Assert.Equal("Bespoke Bō", row.Name);
        Assert.Equal(2, row.LineNumber);
    }

    [Theory]
    [InlineData("Pithing Needle")]
    [InlineData("x Pithing Needle")]
    [InlineData("0 Pithing Needle")]
    [InlineData("2")]
    [InlineData("3 ")]
    [InlineData("3  Island")]
    public void Malformed_decklist_line_is_a_format_error(string line)
    {
        var error = Assert.Throws<FormatException>(() => DecklistParser.Parse(new StringReader(line)));
        Assert.Contains("Line 1", error.Message);
    }

    private static string FixturePath(string file)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "collection-import", file);

    private static IReadOnlyList<ImportRow> ParseFixture(string file)
    {
        using var reader = new StreamReader(FixturePath(file));
        return ManaBoxCsvParser.Parse(reader);
    }
}
