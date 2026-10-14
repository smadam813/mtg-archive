using System.Globalization;
using System.Text;

namespace MtgArchive.CollectionImport;

/// <summary>
/// Parses a ManaBox CSV export (see docs/research/manabox-export-schema.md)
/// into <see cref="ImportRow"/>s. Column mapping is header-driven, never
/// positional, so the 15-column core, the 17-column Binder-prefixed, and the
/// 18-column Added-suffixed variants all parse alike. RFC 4180 mechanics:
/// comma delimiter, double-quoted fields (embedded commas, quotes, and
/// newlines), CRLF or LF line endings, UTF-8 without BOM (a BOM is tolerated
/// and stripped).
///
/// Only Name and Quantity are interpreted; every other column is provenance
/// kept verbatim, so unknown values in enum-ish columns (Foil, Condition,
/// Language, Rarity, …) are never fatal by construction — quarantine exists
/// only for name-resolution failures. Structural corruption (a missing
/// required header, a ragged row, a non-numeric quantity) throws
/// <see cref="FormatException"/> naming the line: a truncated file must fail
/// loudly, not shed rows.
/// </summary>
public static class ManaBoxCsvParser
{
    public static IReadOnlyList<ImportRow> Parse(TextReader reader)
    {
        // Consume a BOM before tokenizing \u2014 stripping it afterwards would be
        // too late for a quoted first header cell, where a leading BOM stops
        // the tokenizer from recognizing the opening quote.
        if (reader.Peek() == '\uFEFF')
        {
            reader.Read();
        }

        var records = ReadRecords(reader);
        if (records.Count == 0)
        {
            throw new FormatException("The CSV has no header row.");
        }

        var header = records[0];
        var nameColumn = RequiredColumn(header, "Name");
        var quantityColumn = RequiredColumn(header, "Quantity");

        var rows = new List<ImportRow>(records.Count - 1);
        foreach (var record in records.Skip(1))
        {
            if (record.Fields.Count != header.Fields.Count)
            {
                throw new FormatException(
                    $"Line {record.LineNumber}: {record.Fields.Count} fields where the header has {header.Fields.Count}.");
            }

            if (!int.TryParse(record.Fields[quantityColumn], NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
                || quantity < 1)
            {
                throw new FormatException(
                    $"Line {record.LineNumber}: Quantity '{record.Fields[quantityColumn]}' is not a positive integer.");
            }

            var provenance = new Dictionary<string, string>(header.Fields.Count);
            for (var i = 0; i < header.Fields.Count; i++)
            {
                provenance[header.Fields[i]] = record.Fields[i];
            }

            rows.Add(new ImportRow
            {
                Name = record.Fields[nameColumn],
                Quantity = quantity,
                LineNumber = record.LineNumber,
                Provenance = provenance,
            });
        }

        return rows;
    }

    private static int RequiredColumn(CsvRecord header, string name)
    {
        var index = header.Fields.IndexOf(name);
        return index >= 0
            ? index
            : throw new FormatException($"Line {header.LineNumber}: the header row has no '{name}' column.");
    }

    private sealed record CsvRecord(int LineNumber, List<string> Fields);

    // RFC 4180 tokenizer. Reads the whole stream character-wise so quoted
    // fields can span commas, doubled quotes, and line breaks.
    private static List<CsvRecord> ReadRecords(TextReader reader)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordStartLine = 1;
        var inQuotes = false;
        var recordHasContent = false;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void EndRecord()
        {
            EndField();
            records.Add(new CsvRecord(recordStartLine, fields));
            fields = [];
            recordHasContent = false;
        }

        int read;
        while ((read = reader.Read()) >= 0)
        {
            var c = (char)read;
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    recordHasContent = true;
                    break;
                case ',':
                    EndField();
                    recordHasContent = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (recordHasContent || field.Length > 0)
                    {
                        EndRecord();
                    }

                    line++;
                    recordStartLine = line;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (inQuotes)
        {
            throw new FormatException($"Line {recordStartLine}: unterminated quoted field.");
        }

        if (recordHasContent || field.Length > 0)
        {
            EndRecord();
        }

        return records;
    }
}
