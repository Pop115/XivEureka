using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace XivEureka.Tracker;

/// <summary>
/// A Phoenix Channels message (protocol of the ffxiv-eureka.com server, version 2.0.0):
/// <c>[join_ref, ref, topic, event, payload]</c>.
/// </summary>
public sealed record PhoenixMessage(string? JoinRef, string? Ref, string Topic, string Event, JsonElement Payload);

/// <summary>
/// A ffxiv-eureka.com tracker as the server describes it: its zone (site ids: 1 Anemos, 2 Pagos,
/// 3 Pyros, 4 Hydatos), its data center, each NM's kill time (site NM ids) and the prepped NMs.
/// <see cref="Password"/> is only known for a tracker we created.
/// </summary>
public sealed record TrackerInstance(string Id, int? Zone, int? DataCenter, IReadOnlyDictionary<int, DateTimeOffset> Kills,
    IReadOnlySet<int> Prepped, string? Password, DateTimeOffset? UpdatedAt);

/// <summary>Reads and writes tracker messages, without the network (testable on captures).</summary>
public static class TrackerFormat
{
    /// <summary>The site's data centers (ffxiv-eureka.com db/data-centers): English game name → id.</summary>
    public static readonly IReadOnlyDictionary<string, int> DataCenters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Elemental"] = 1, ["Gaia"] = 2, ["Mana"] = 3, ["Aether"] = 4, ["Primal"] = 5, ["Chaos"] = 6,
        ["中国区"] = 7, ["中国二区"] = 8, ["한국"] = 9, ["Crystal"] = 10, ["Light"] = 11, ["Materia"] = 12,
        ["Dynamis"] = 13, ["Meteor"] = 14,
    };

    /// <summary>The site's zone for a Eureka territory; null elsewhere.</summary>
    public static int? SiteZone(uint territory) => territory switch
    {
        732 => 1, 763 => 2, 795 => 3, 827 => 4, _ => null,
    };

    public static PhoenixMessage? Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() != 5) return null;
            return new PhoenixMessage(StringOrNull(r[0]), StringOrNull(r[1]), r[2].GetString() ?? string.Empty,
                r[3].GetString() ?? string.Empty, r[4].Clone());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Write(string? joinRef, string? @ref, string topic, string @event, object payload) =>
        JsonSerializer.Serialize(new object?[] { joinRef, @ref, topic, @event, payload });

    /// <summary>A full tracker (the <c>data</c> object of <c>initial_payload</c> or of the creation response).</summary>
    public static TrackerInstance ReadInstance(JsonElement data) =>
        Merge(new TrackerInstance(data.GetProperty("id").GetString() ?? string.Empty, ZoneOf(data), null,
            new Dictionary<int, DateTimeOffset>(), new HashSet<int>(), null, null), data);

    /// <summary>
    /// Applies an update (<c>payload</c>): the server only sends the attributes that changed, the
    /// others stay.
    /// </summary>
    public static TrackerInstance Merge(TrackerInstance before, JsonElement data)
    {
        if (!data.TryGetProperty("attributes", out var a)) return before;
        var after = before;
        if (a.TryGetProperty("notorious-monsters", out var kills)) after = after with { Kills = ReadKills(kills) };
        if (a.TryGetProperty("prepped-notorious-monsters", out var prep)) after = after with { Prepped = ReadPrepped(prep) };
        if (a.TryGetProperty("data-center-id", out var dc)) after = after with { DataCenter = Int(dc) };
        if (a.TryGetProperty("password", out var pw) && pw.ValueKind == JsonValueKind.String) after = after with { Password = pw.GetString() };
        if (a.TryGetProperty("updated-at", out var updated) && updated.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updated.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
            after = after with { UpdatedAt = when };
        return after;
    }

    /// <summary>JSON:API body to create a tracker (as the site does: <c>POST /api/instances</c>).</summary>
    public static string CreationBody(int siteZone) =>
        JsonSerializer.Serialize(new { data = new { type = "instances", attributes = new Dictionary<string, object?> { ["copy-from"] = null, ["zone-id"] = siteZone.ToString(CultureInfo.InvariantCulture) } } });

    private static int? ZoneOf(JsonElement data) =>
        data.TryGetProperty("relationships", out var rel) && rel.TryGetProperty("zone", out var z)
        && z.TryGetProperty("data", out var zd) && zd.ValueKind == JsonValueKind.Object && zd.TryGetProperty("id", out var id)
            ? Int(id) : null;

    // "{"38":1792015799985}": a JSON string inside the JSON; values in milliseconds (number or text).
    private static Dictionary<int, DateTimeOffset> ReadKills(JsonElement raw)
    {
        var result = new Dictionary<int, DateTimeOffset>();
        if (Inner(raw) is not { } doc) return result;
        using (doc)
            foreach (var p in doc.RootElement.EnumerateObject())
                if (int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nm) && Long(p.Value) is { } ms)
                    result[nm] = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return result;
    }

    private static HashSet<int> ReadPrepped(JsonElement raw)
    {
        var result = new HashSet<int>();
        if (Inner(raw) is not { } doc) return result;
        using (doc)
            foreach (var p in doc.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.True))
                if (int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nm)) result.Add(nm);
        return result;
    }

    private static JsonDocument? Inner(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.String) return null;
        try
        {
            var doc = JsonDocument.Parse(raw.GetString() ?? "{}");
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
            doc.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringOrNull(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static int? Int(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetInt32(out var n) => n,
        JsonValueKind.String when int.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    private static long? Long(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetInt64(out var n) => n,
        JsonValueKind.String when long.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };
}
