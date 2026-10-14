using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Tests;

/// <summary>
/// The no-illegal-candidates property harness (ADR 0001). Index endpoints
/// are discovered from the host's route table — every POST whose body
/// derives from <see cref="CandidateQueryRequest"/> — so an endpoint
/// landing later (card search, collection scope) is fuzzed the moment it
/// maps, with no per-endpoint test code (commander alternatives is the one
/// candidate query outside the base type: its role is fixed, so
/// <c>CommanderAlternativesTests</c> covers the boundary). Each probe
/// fuzzes the shared inputs (a materialized <c>format</c> or none, a known
/// <c>banlist_version</c> pin or none in Commander, <c>role</c>, <c>color_identity</c>,
/// <c>collection_scope</c>) and, by
/// reflection over the request type, the endpoint's own members from a
/// corpus-drawn vocabulary. Every 2xx response must echo the format and
/// the version used (the banlist version in Commander, the legality as-of
/// date in a Brawl format) and carry a top-level <c>candidates</c> array whose elements cite their
/// card under <c>card</c>; each cited card is checked against the real
/// materialized legality table and the real collection through
/// <see cref="MaterializedLegality"/> — under <c>collection_scope</c> an
/// unowned candidate is a violation, and a reported <c>available</c> must
/// be min(copy limit, owned quantity). A 5xx, a 2xx without
/// <c>candidates</c>, or an endpoint that rejects more than half its probes
/// or never returns a candidate is a violation too: nothing escapes by
/// never being exercised.
/// </summary>
internal static class IndexFuzzHarness
{
    public sealed record Violation(string Route, string Request, string Detail);

    public sealed record Report(IReadOnlyList<string> Routes, int Probes, IReadOnlyList<Violation> Violations)
    {
        public string Describe() => string.Join(Environment.NewLine,
            new[] { $"{Violations.Count} violation(s) over {Probes} probes of [{string.Join(", ", Routes)}]" }
                .Concat(Violations.Select(v => $"{v.Route} {v.Request}: {v.Detail}")));
    }

    public static IReadOnlyList<(string Route, Type RequestType)> Discover(WebApplicationFactory<Program> app)
        => [.. app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Route: e.RoutePattern.RawText!, e.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType))
            .Where(e => e.RequestType is not null && typeof(CandidateQueryRequest).IsAssignableFrom(e.RequestType))
            .Select(e => (e.Route, e.RequestType!))
            .OrderBy(e => e.Route, StringComparer.Ordinal)];

    public static async Task<Report> RunAsync(
        WebApplicationFactory<Program> app,
        string connectionString,
        int seed,
        bool alwaysColorIdentity,
        bool alwaysCollectionScope = false,
        int probesPerEndpoint = 60)
    {
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        var rng = new Random(seed);
        using var client = app.CreateClient();
        var endpoints = Discover(app);
        var violations = new List<Violation>();
        var probes = 0;
        foreach (var (route, requestType) in endpoints)
        {
            // Probes post to the route verbatim; a templated route would
            // 404 every one, so it fails here — inputs belong in the body.
            if (route.Contains('{'))
            {
                violations.Add(new(route, "", "route has parameters; the harness fuzzes body-only POST routes"));
                continue;
            }

            // Bodies are drawn first so the seed decides them; the posts
            // then fan out, and results land by probe index to keep the
            // report's order deterministic.
            var fuzzed = Enumerable.Range(0, probesPerEndpoint)
                .Select(_ => Fuzz(rng, oracle, requestType, alwaysColorIdentity, alwaysCollectionScope))
                .ToList();
            var results = new ProbeResult[probesPerEndpoint];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, probesPerEndpoint),
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                async (i, _) => results[i] = await ProbeAsync(client, oracle, route, fuzzed[i]));
            probes += probesPerEndpoint;
            violations.AddRange(results.SelectMany(r => r.Violations));
            var accepted = results.Count(r => r.Accepted);
            var returned = results.Sum(r => r.Returned);
            if (accepted * 2 < probesPerEndpoint || returned == 0)
            {
                violations.Add(new(route, "", $"only {accepted} of {probesPerEndpoint} fuzzed requests were accepted "
                    + $"and {returned} candidates came back; the endpoint was barely exercised"));
            }
        }

        return new Report([.. endpoints.Select(e => e.Route)], probes, violations);
    }

    private sealed record ProbeResult(bool Accepted, int Returned, IReadOnlyList<Violation> Violations);

    private static async Task<ProbeResult> ProbeAsync(
        HttpClient client, MaterializedLegality oracle, string route, FuzzedProbe probe)
    {
        var (body, format, version, role, identity, scoped) = probe;
        var json = body.ToJsonString();
        using var response = await client.PostAsync(
            route, new StringContent(json, Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode >= 500)
        {
            return new(false, 0, [new(route, json, $"{(int)response.StatusCode}: {text}")]);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new(false, 0, []);
        }

        var violations = new List<Violation>();
        using var doc = JsonDocument.Parse(text);
        var expectedFormat = format ?? Format.Commander;
        var commander = expectedFormat == Format.Commander;
        // A Brawl format has no banlist version: its legality rows carry
        // none, and the versions block echoes none.
        var expectedVersion = commander ? version ?? oracle.Latest : null;
        var hasVersions = doc.RootElement.TryGetProperty("versions", out var versions);
        if (!hasVersions
            || !versions.TryGetProperty("format", out var echoedFormat)
            || echoedFormat.GetString() != FormatParameter.WireName(expectedFormat))
        {
            violations.Add(new(route, json, $"versions.format must echo '{FormatParameter.WireName(expectedFormat)}'"));
        }

        var echoedVersion = hasVersions && versions.TryGetProperty("banlist_version", out var echoed)
            ? echoed.GetString()
            : null;
        if (echoedVersion != expectedVersion)
        {
            violations.Add(new(route, json, expectedVersion is null
                ? "versions.banlist_version must be absent in a Brawl format"
                : $"versions.banlist_version must echo '{expectedVersion}'"));
        }

        // The legality as-of date dates a Brawl format and only a Brawl format.
        var hasAsOf = hasVersions && versions.TryGetProperty("legality_as_of", out _);
        if (hasAsOf == commander)
        {
            violations.Add(new(route, json, commander
                ? "versions.legality_as_of must be absent in commander"
                : "versions.legality_as_of must date a Brawl format"));
        }

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates))
        {
            violations.Add(new(route, json, "2xx response without a top-level candidates array"));
            return new(true, 0, violations);
        }

        var returned = 0;
        foreach (var candidate in candidates.EnumerateArray())
        {
            returned++;
            var id = Guid.Parse(candidate.GetProperty("card").GetProperty("id").GetString()!);
            if (oracle.Objection(id, expectedFormat, expectedVersion, role, identity, scoped) is { } objection)
            {
                violations.Add(new(route, json, objection));
            }

            if (scoped
                && candidate.TryGetProperty("available", out var available)
                && available.GetInt32() != oracle.Available(id))
            {
                violations.Add(new(route, json,
                    $"{id} available {available.GetInt32()} must be {oracle.Available(id)?.ToString() ?? "null"}"));
            }
        }

        return new(true, returned, violations);
    }

    private static readonly string[] Colors = ["W", "U", "B", "R", "G"];

    private sealed record FuzzedProbe(
        JsonObject Body, Format? Format, string? Version, Role Role, string[]? Identity, bool CollectionScope);

    private static FuzzedProbe Fuzz(
        Random rng, MaterializedLegality oracle, Type requestType, bool alwaysColorIdentity, bool alwaysCollectionScope)
    {
        var body = new JsonObject();
        // The format axis: each materialized format, or none (Commander).
        var formats = FormatParameter.Materialized;
        Format? format = rng.Next(formats.Count + 1) is var pick && pick < formats.Count ? formats[pick] : null;
        if (format is { } named)
        {
            body["format"] = FormatParameter.WireName(named);
        }

        // A banlist version pin is Commander's axis only; a Brawl format with
        // a pin is a 400 (BrawlCandidateTests), so the harness never sends one.
        var commander = (format ?? Format.Commander) == Format.Commander;
        var version = !commander || rng.Next(3) == 0 ? null : oracle.Versions[rng.Next(oracle.Versions.Count)];
        if (version is not null)
        {
            body["banlist_version"] = version;
        }

        var roles = Enum.GetValues<Role>();
        var role = roles[rng.Next(roles.Length)];
        body["role"] = JsonSerializer.SerializeToNode(role, WireJson.Options);
        var identity = alwaysColorIdentity || rng.Next(2) == 0
            ? Colors.Where(_ => rng.Next(2) == 0).ToArray()
            : null;
        if (identity is not null)
        {
            body["color_identity"] = new JsonArray([.. identity.Select(c => (JsonNode)c)]);
        }

        var scoped = alwaysCollectionScope || rng.Next(2) == 0;
        body["collection_scope"] = scoped;

        // The probe always sends the endpoint's required members. It sends
        // each other member a third of the time, drawn by type from the
        // corpus vocabulary. Every member narrows the result, so at a higher
        // rate most probes return no cards once an endpoint has many members.
        foreach (var property in requestType.GetProperties()
            .Where(p => p.DeclaringType != typeof(CandidateQueryRequest)))
        {
            var required = property.GetCustomAttribute<RequiredMemberAttribute>() is not null;
            if (required || rng.Next(3) == 0)
            {
                body[JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name)] =
                    RandomValue(rng, oracle, property.PropertyType, depth: 0);
            }
        }

        return new(body, format, version, role, identity, scoped);
    }

    private static JsonNode? RandomValue(Random rng, MaterializedLegality oracle, Type type, int depth)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(bool))
        {
            return rng.Next(2) == 0;
        }

        if (type == typeof(int) || type == typeof(decimal))
        {
            return rng.Next(1, 61);
        }

        if (type == typeof(string))
        {
            return oracle.Word(rng);
        }

        if (type == typeof(Guid))
        {
            return oracle.OracleId(rng);
        }

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return JsonSerializer.SerializeToNode(values.GetValue(rng.Next(values.Length)), type, WireJson.Options);
        }

        if (type.IsAssignableTo(typeof(IEnumerable)))
        {
            var element = type.IsArray ? type.GetElementType()
                : type.GetGenericArguments() is [var single] ? single : null;
            // An id list holds about half the corpus ids. It narrows the
            // result but leaves cards to return. It also includes stale and
            // illegal ids, which the boundary must drop.
            if (element == typeof(Guid))
            {
                return new JsonArray([.. oracle.OracleIds.Where(_ => rng.Next(2) == 0).Select(id => (JsonNode)id.ToString())]);
            }

            return element is null
                ? null
                : new JsonArray([.. Enumerable.Range(0, rng.Next(3)).Select(_ => RandomValue(rng, oracle, element, depth))]);
        }

        if (depth < 2 && type.IsClass)
        {
            var nested = new JsonObject();
            foreach (var property in type.GetProperties())
            {
                nested[JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name)] =
                    RandomValue(rng, oracle, property.PropertyType, depth + 1);
            }

            return nested;
        }

        return null;
    }
}
