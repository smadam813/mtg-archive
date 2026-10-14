using System.Text.Json;

namespace MtgArchive.Ingest;

/// <summary>
/// Parses and validates the three downloaded Scryfall bulk JSONL files.
/// Validation completes before any DB write: the readers walk every line
/// of every file, and every problem lands in the caller's error list
/// (<see cref="IngestRunner"/> caps the list for the report and announces
/// the cap — never a silent cut). A snapshot with errors never reaches the
/// card upsert phase.
/// </summary>
public static class ScryfallSnapshotReader
{
    /// <summary>Non-game objects whose names shadow real cards;
    /// tokens stay because collection imports resolve them.</summary>
    private static readonly HashSet<string> ExcludedLayouts = ["art_series", "front_card"];

    public static IReadOnlyList<SnapshotCard> ReadCards(string path, List<string> errors)
    {
        var cards = new List<SnapshotCard>();
        var seen = new HashSet<Guid>();
        foreach (var (document, line, lineNumber) in JsonLines(path, "oracle-cards", errors))
        {
            using (document)
            {
                var card = ParseCard(document.RootElement, line, lineNumber, errors);
                if (card is null)
                {
                    continue;
                }

                if (!seen.Add(card.OracleId))
                {
                    errors.Add($"oracle-cards line {lineNumber}: duplicate oracle_id {card.OracleId}");
                    continue;
                }

                cards.Add(card);
            }
        }

        return cards;
    }

    private static SnapshotCard? ParseCard(JsonElement root, string line, int lineNumber, List<string> errors)
    {
        var missing = new List<string>();
        var oracleId = GetGuid(root, "oracle_id", missing);
        var name = GetString(root, "name", missing);
        var typeLine = GetString(root, "type_line", missing, allowEmpty: true);
        var cmc = GetDecimal(root, "cmc", missing);
        var releasedAt = GetDate(root, "released_at", missing);
        var colorIdentity = OptionalStringArray(root, "color_identity", missing);
        if (colorIdentity is null && !missing.Contains("color_identity"))
        {
            missing.Add("color_identity");
        }

        var colors = OptionalStringArray(root, "colors", missing);
        var keywords = OptionalStringArray(root, "keywords", missing);
        var producedMana = OptionalStringArray(root, "produced_mana", missing);
        if (missing.Count > 0)
        {
            errors.Add($"oracle-cards line {lineNumber}: missing or invalid {string.Join(", ", missing)}");
            return null;
        }

        var layout = OptionalString(root, "layout");
        if (layout is not null && ExcludedLayouts.Contains(layout))
        {
            return null;
        }

        return new SnapshotCard
        {
            OracleId = oracleId!.Value,
            Name = name!,
            Layout = layout,
            TypeLine = typeLine!,
            ManaCost = OptionalString(root, "mana_cost"),
            Cmc = cmc!.Value,
            OracleText = OptionalString(root, "oracle_text"),
            Colors = colors,
            ColorIdentity = colorIdentity!,
            Keywords = keywords ?? [],
            Power = OptionalString(root, "power"),
            Toughness = OptionalString(root, "toughness"),
            Loyalty = OptionalString(root, "loyalty"),
            ProducedMana = producedMana,
            GameChanger = root.TryGetProperty("game_changer", out var gc) && gc.ValueKind == JsonValueKind.True,
            IsMeldResult = IsMeldResult(root, name!),
            CommanderLegality = root.TryGetProperty("legalities", out var legalities)
                && legalities.ValueKind == JsonValueKind.Object
                && legalities.TryGetProperty("commander", out var commander)
                && commander.ValueKind == JsonValueKind.String
                ? commander.GetString()
                : null,
            Digital = root.TryGetProperty("digital", out var digital)
                && digital.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? digital.GetBoolean()
                : null,
            EdhrecRank = root.TryGetProperty("edhrec_rank", out var rank) && rank.ValueKind == JsonValueKind.Number
                && rank.TryGetInt32(out var edhrecRank)
                ? edhrecRank
                : null,
            ReleasedAt = releasedAt!.Value,
            Faces = ParseFaces(root),
            RawJson = line,
        };
    }

    private static bool IsMeldResult(JsonElement root, string name)
        => root.TryGetProperty("all_parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array
            && parts.EnumerateArray().Any(part =>
                part.ValueKind == JsonValueKind.Object
                && OptionalString(part, "component") == "meld_result" && OptionalString(part, "name") == name);

    private static IReadOnlyList<SnapshotFace> ParseFaces(JsonElement root)
    {
        if (!root.TryGetProperty("card_faces", out var faces) || faces.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. faces.EnumerateArray().Select(face => new SnapshotFace
        {
            Name = OptionalString(face, "name") ?? "",
            ManaCost = OptionalString(face, "mana_cost"),
            TypeLine = OptionalString(face, "type_line") ?? "",
            OracleText = OptionalString(face, "oracle_text"),
            Colors = OptionalStringArray(face, "colors"),
            Power = OptionalString(face, "power"),
            Toughness = OptionalString(face, "toughness"),
            Loyalty = OptionalString(face, "loyalty"),
        })];
    }

    public static IReadOnlyDictionary<Guid, List<SnapshotRuling>> ReadRulings(string path, List<string> errors)
    {
        var rulings = new Dictionary<Guid, List<SnapshotRuling>>();
        foreach (var (document, _, lineNumber) in JsonLines(path, "rulings", errors))
        {
            using (document)
            {
                var root = document.RootElement;
                var missing = new List<string>();
                var oracleId = GetGuid(root, "oracle_id", missing);
                var source = GetString(root, "source", missing);
                var publishedAt = GetDate(root, "published_at", missing);
                var comment = GetString(root, "comment", missing);
                if (missing.Count > 0)
                {
                    errors.Add($"rulings line {lineNumber}: missing or invalid {string.Join(", ", missing)}");
                    continue;
                }

                if (!rulings.TryGetValue(oracleId!.Value, out var list))
                {
                    rulings[oracleId.Value] = list = [];
                }

                list.Add(new SnapshotRuling(source!, publishedAt!.Value, comment!));
            }
        }

        return rulings;
    }

    /// <summary>
    /// Each tagging lands as the tag's own label plus every transitive
    /// ancestor through <c>parent_ids</c>, a direct tagging winning over the
    /// same label inherited. A cycle or an unknown parent id is a hierarchy
    /// anomaly, not a validation error: it goes to <paramref name="anomalies"/>
    /// (the ingest report), the tag's taggings still land, and the run goes
    /// on. Cards never carry an alias. The alias map comes back beside the
    /// taggings for the <c>tags</c> filter: first claim wins, a second claim
    /// is an anomaly.
    /// </summary>
    public static SnapshotTags ReadOracleTags(string path, List<string> errors, List<string> anomalies)
    {
        var records = new List<TagRecord>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (document, _, lineNumber) in JsonLines(path, "oracle-tags", errors))
        {
            using (document)
            {
                var root = document.RootElement;
                var label = OptionalString(root, "label") ?? OptionalString(root, "slug");
                if (label is null)
                {
                    errors.Add($"oracle-tags line {lineNumber}: missing or invalid label");
                    continue;
                }

                if (!root.TryGetProperty("taggings", out var taggings) || taggings.ValueKind != JsonValueKind.Array)
                {
                    errors.Add($"oracle-tags line {lineNumber}: missing or invalid taggings");
                    continue;
                }

                var parsed = new List<(Guid OracleId, string? Weight)>();
                foreach (var tagging in taggings.EnumerateArray())
                {
                    var missing = new List<string>();
                    var oracleId = GetGuid(tagging, "oracle_id", missing);
                    if (missing.Count > 0)
                    {
                        errors.Add($"oracle-tags line {lineNumber}: tagging with missing or invalid oracle_id");
                        continue;
                    }

                    parsed.Add((oracleId!.Value, OptionalString(tagging, "weight")));
                }

                var invalid = new List<string>();
                var parentIds = OptionalStringArray(root, "parent_ids", invalid) ?? [];
                var aliases = OptionalStringArray(root, "aliases", invalid) ?? [];
                if (invalid.Count > 0)
                {
                    errors.Add($"oracle-tags line {lineNumber}: missing or invalid {string.Join(", ", invalid)}");
                    continue;
                }

                var id = OptionalString(root, "id");
                if (id is not null && !seenIds.Add(id))
                {
                    errors.Add($"oracle-tags line {lineNumber}: duplicate id {id}");
                    continue;
                }

                records.Add(new TagRecord(lineNumber, id, label, parentIds, aliases, parsed));
            }
        }

        var byId = records.Where(r => r.Id is not null).ToDictionary(r => r.Id!, r => r);
        // One line per missing id, attributed to its first referrer, so a
        // parent absent from the export is one report entry however many
        // tags name it.
        foreach (var unknown in records
            .SelectMany(r => r.ParentIds.Where(id => !byId.ContainsKey(id)).Select(id => (Record: r, Id: id)))
            .GroupBy(p => p.Id, StringComparer.Ordinal))
        {
            var first = unknown.First().Record;
            var others = unknown.Count() - 1;
            anomalies.Add($"oracle-tags line {first.LineNumber}: tag '{first.Label}' names unknown parent id {unknown.Key}"
                + (others > 0 ? $" ({others} more tag(s) name it)" : ""));
        }

        var tags = new Dictionary<Guid, Dictionary<string, SnapshotTag>>();
        foreach (var record in records)
        {
            var ancestors = Ancestors(record, byId, anomalies);
            foreach (var (oracleId, weight) in record.Taggings)
            {
                if (!tags.TryGetValue(oracleId, out var cardTags))
                {
                    tags[oracleId] = cardTags = new Dictionary<string, SnapshotTag>(StringComparer.Ordinal);
                }

                if (!cardTags.TryGetValue(record.Label, out var current) || current.Inherited)
                {
                    cardTags[record.Label] = new SnapshotTag(record.Label, false, weight);
                }

                foreach (var ancestor in ancestors)
                {
                    cardTags.TryAdd(ancestor, new SnapshotTag(ancestor, true, null));
                }
            }
        }

        return new SnapshotTags(
            tags.ToDictionary(pair => pair.Key, pair => pair.Value.Values.ToList()),
            AliasMap(records, anomalies));
    }

    /// <summary>Alias to label in file order, first claim winning. The map
    /// skips an alias that equals a label: the label matches as itself, and
    /// the filter must not widen it. A second claim on one alias is an
    /// anomaly, not an error.</summary>
    private static Dictionary<string, string> AliasMap(List<TagRecord> records, List<string> anomalies)
    {
        var labels = records.Select(r => r.Label).ToHashSet(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            foreach (var alias in record.Aliases.Where(a => !labels.Contains(a)))
            {
                if (aliases.TryGetValue(alias, out var first))
                {
                    if (first != record.Label)
                    {
                        anomalies.Add(
                            $"oracle-tags line {record.LineNumber}: alias '{alias}' on tag '{record.Label}' already names tag '{first}'");
                    }

                    continue;
                }

                aliases[alias] = record.Label;
            }
        }

        return aliases;
    }

    /// <summary>Transitive parent labels, own label excluded. The visited
    /// set makes a cycle terminate; reaching the record itself names it. An
    /// unknown parent id was already reported against the record that owns
    /// it, so the walk just stops there.</summary>
    private static HashSet<string> Ancestors(TagRecord record, Dictionary<string, TagRecord> byId, List<string> anomalies)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(record.ParentIds);
        while (pending.Count > 0)
        {
            var parentId = pending.Pop();
            if (!visited.Add(parentId))
            {
                continue;
            }

            if (parentId == record.Id)
            {
                anomalies.Add($"oracle-tags line {record.LineNumber}: tag '{record.Label}' is in a parent_ids cycle");
                continue;
            }

            if (!byId.TryGetValue(parentId, out var parent))
            {
                continue;
            }

            labels.Add(parent.Label);
            foreach (var grandparent in parent.ParentIds)
            {
                pending.Push(grandparent);
            }
        }

        return labels;
    }

    private sealed record TagRecord(
        int LineNumber,
        string? Id,
        string Label,
        string[] ParentIds,
        string[] Aliases,
        List<(Guid OracleId, string? Weight)> Taggings);

    /// <summary>
    /// Walks one JSONL file: skips blank lines, itemizes unparseable ones,
    /// and yields each parsed line for the caller to dispose.
    /// </summary>
    private static IEnumerable<(JsonDocument Document, string Line, int LineNumber)> JsonLines(
        string path, string file, List<string> errors)
    {
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                errors.Add($"{file} line {lineNumber}: invalid JSON ({ex.Message})");
                continue;
            }

            yield return (document, line, lineNumber);
        }
    }

    private static Guid? GetGuid(JsonElement element, string name, List<string> missing)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.TryGetGuid(out var guid))
        {
            return guid;
        }

        missing.Add(name);
        return null;
    }

    private static string? GetString(JsonElement element, string name, List<string> missing, bool allowEmpty = false)
    {
        var value = OptionalString(element, name, allowEmpty ? "" : null);
        if (value is null)
        {
            missing.Add(name);
        }

        return value;
    }

    private static decimal? GetDecimal(JsonElement element, string name, List<string> missing)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDecimal();
        }

        missing.Add(name);
        return null;
    }

    private static DateOnly? GetDate(JsonElement element, string name, List<string> missing)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(value.GetString(), out var date))
        {
            return date;
        }

        missing.Add(name);
        return null;
    }

    /// <summary>Empty strings normalize to null (Scryfall uses "" for e.g. a land's mana cost).</summary>
    private static string? OptionalString(JsonElement element, string name, string? whenEmpty = null)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            return text.Length > 0 ? text : whenEmpty;
        }

        return null;
    }

    /// <summary>
    /// Null when the field is absent; a present-but-malformed array (or any
    /// non-string element) lands in <paramref name="invalid"/> so schema
    /// drift itemizes instead of silently dropping field data.
    /// </summary>
    private static string[]? OptionalStringArray(JsonElement element, string name, List<string>? invalid = null)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array
            || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            invalid?.Add(name);
            return null;
        }

        return [.. value.EnumerateArray().Select(item => item.GetString()!)];
    }
}
