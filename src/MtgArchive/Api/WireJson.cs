using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgArchive.Api;

/// <summary>
/// The one wire format: snake_case everywhere — property names, dictionary
/// keys, and enum values (<c>oracle_id</c>, <c>banlist_version</c>,
/// <c>ban_event</c>). The serializer omits null members so responses stay lean.
/// </summary>
public static class WireJson
{
    /// <summary>Ready-made options for serializing outside the HTTP pipeline
    /// (e.g. report jsonb that must match the wire shape).</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions();
        Apply(options);
        return options;
    }

    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }
}
