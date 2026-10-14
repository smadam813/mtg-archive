using System.Text.Json;

namespace MtgArchive.Api;

/// <summary>
/// Shared shape checks for the goal-predicate and disruption-profile parsers
/// and the bracket files. Every check appends a prefixed, path-qualified
/// message to the error list and returns whether the value passed; callers
/// keep going so one file reports every problem.
/// </summary>
internal sealed class JsonShape(string file, List<string> errors)
{
    public void Fail(string path, string message) => errors.Add($"{file}: {path}: {message}");

    public int ErrorCount => errors.Count;

    public static JsonDocument? Parse(string file, string json, List<string> errors)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errors.Add($"{file}: invalid JSON — {ex.Message}");
            return null;
        }
    }

    public bool IsObject(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        Fail(path, "must be an object");
        return false;
    }

    /// <summary>Reports every property not in <paramref name="allowed"/>.
    /// <c>$schema</c> is tolerated at the root for editor tooling.</summary>
    public void OnlyKeys(JsonElement obj, string path, IEnumerable<string> allowed, bool isRoot = false)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
        {
            if (isRoot && property.Name == "$schema")
            {
                continue;
            }

            if (!set.Contains(property.Name))
            {
                Fail(path, $"unknown property '{property.Name}' (allowed: {string.Join(", ", set.Order(StringComparer.Ordinal))})");
            }
        }
    }

    public bool Required(JsonElement obj, string path, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
        {
            return true;
        }

        Fail(path, $"missing required property '{name}'");
        return false;
    }

    public string? String(JsonElement obj, string path, string name, bool required = true)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            if (required)
            {
                Fail(path, $"missing required property '{name}'");
            }

            return null;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            Fail($"{path}.{name}", "must be a non-empty string");
            return null;
        }

        return value.GetString();
    }

    public bool? Bool(JsonElement obj, string path, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        Fail($"{path}.{name}", "must be a boolean");
        return null;
    }

    public int? Int(JsonElement obj, string path, string name, int min, bool required = true)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            if (required)
            {
                Fail(path, $"missing required property '{name}'");
            }

            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min)
        {
            Fail($"{path}.{name}", $"must be an integer >= {min}");
            return null;
        }

        return number;
    }

    public string? Enum(JsonElement obj, string path, string name, IEnumerable<string> values, bool required = true)
    {
        var text = String(obj, path, name, required);
        if (text is null)
        {
            return null;
        }

        var set = values.ToList();
        if (!set.Contains(text, StringComparer.Ordinal))
        {
            Fail($"{path}.{name}", $"'{text}' is not one of: {string.Join(", ", set)}");
            return null;
        }

        return text;
    }

    public bool Array(JsonElement obj, string path, string name, out JsonElement array, bool required = true, bool nonEmpty = false)
    {
        array = default;
        if (!obj.TryGetProperty(name, out var value))
        {
            if (required)
            {
                Fail(path, $"missing required property '{name}'");
            }

            return false;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            Fail($"{path}.{name}", "must be an array");
            return false;
        }

        if (nonEmpty && value.GetArrayLength() == 0)
        {
            Fail($"{path}.{name}", "must not be empty");
            return false;
        }

        array = value;
        return true;
    }

    /// <summary>An array of non-empty strings; returns the strings that passed.</summary>
    public List<string> StringArray(JsonElement obj, string path, string name, bool required = true, bool nonEmpty = false)
    {
        var result = new List<string>();
        if (!Array(obj, path, name, out var array, required, nonEmpty))
        {
            return result;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                Fail($"{path}.{name}[{index}]", "must be a non-empty string");
            }
            else
            {
                result.Add(item.GetString()!);
            }

            index++;
        }

        return result;
    }
}
