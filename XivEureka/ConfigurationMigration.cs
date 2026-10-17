using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace XivEureka;

/// <summary>
/// Reads a version 1 configuration (French property and class names, written before the code moved
/// to English on 2026-10-15) into the current <see cref="Configuration"/>. Dalamud stores the class
/// names (<c>$type</c>), so it cannot load the old file itself once the classes are renamed: this
/// reads the raw JSON, ignoring <c>$type</c>, and copies every value across.
/// </summary>
public static class ConfigurationMigration
{
    /// <summary>True for a file written by version 1 (no Version, or Version below 2).</summary>
    public static bool IsLegacy(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && (!doc.RootElement.TryGetProperty("Version", out var v) || !v.TryGetInt32(out var n) || n < 2);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static Configuration FromLegacy(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var c = new Configuration();

        foreach (var (key, value) in Entries(r, "Personnages"))
        {
            if (!ulong.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var contentId)) continue;
            c.Characters[contentId] = new CharacterState
            {
                Name = Text(value, "Nom") ?? string.Empty,
                Stock = Entries(value, "Stock")
                    .Where(e => uint.TryParse(e.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) && e.Value.ValueKind == JsonValueKind.Number)
                    .ToDictionary(e => uint.Parse(e.Key, CultureInfo.InvariantCulture), e => e.Value.GetInt32()),
                StockReadAt = Date(value, "StockLuLe"),
                Learned = Values(value, "Apprises").Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetUInt32()).ToHashSet(),
                LearnedReadAt = Date(value, "ApprisesLuesLe"),
            };
        }

        c.OpenWithManipulator = Bool(r, "OuvrirAvecLeManipulateur") ?? c.OpenWithManipulator;
        c.AlertFates = Bool(r, "AlerterFates") ?? c.AlertFates;
        c.FlagOnFate = Bool(r, "DrapeauSurFate") ?? c.FlagOnFate;
        c.ListenToChat = Bool(r, "EcouterChat") ?? c.ListenToChat;
        c.FlagOnChatPop = Bool(r, "DrapeauSurChat") ?? c.FlagOnChatPop;
        c.AlertSound = Bool(r, "SonAlertes") ?? c.AlertSound;
        c.FollowShoutedTracker = Bool(r, "SuivreTrackerCrie") ?? c.FollowShoutedTracker;
        c.ReportKills = Bool(r, "SignalerMorts") ?? c.ReportKills;
        c.LastTracker = Text(r, "DernierTracker");
        if (r.TryGetProperty("DerniereZone", out var zone) && zone.TryGetUInt32(out var z)) c.LastZone = z;

        if (r.TryGetProperty("Calques", out var layers) && layers.ValueKind == JsonValueKind.Object)
        {
            c.Layers.Points = Bool(layers, "Reperes") ?? true;
            c.Layers.AreaNames = Bool(layers, "NomsDeZone") ?? true;
            c.Layers.Party = Bool(layers, "Groupe") ?? true;
            c.Layers.Fates = Bool(layers, "Fates") ?? true;
            c.Layers.Pops = Bool(layers, "Pops") ?? true;
            c.Layers.Nms = Bool(layers, "Nm") ?? true;
            c.Layers.Prep = Bool(layers, "Prepa") ?? true;
        }

        foreach (var (id, password) in Entries(r, "MotsDePasseTrackers"))
            if (password.ValueKind == JsonValueKind.String) c.TrackerPasswords[id] = password.GetString()!;

        if (r.TryGetProperty("TrackerEnCours", out var t) && t.ValueKind == JsonValueKind.Object && Text(t, "Id") is { } trackerId)
            c.CurrentTracker = new SavedTracker
            {
                Id = trackerId,
                Territory = t.TryGetProperty("Territoire", out var te) && te.TryGetUInt32(out var tv) ? tv : 0,
                Since = Date(t, "Depuis") ?? DateTime.MinValue,
            };

        return c;
    }

    // Newtonsoft dictionaries come with a "$type" entry: skip it.
    private static IEnumerable<(string Key, JsonElement Value)> Entries(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var o) && o.ValueKind == JsonValueKind.Object
            ? o.EnumerateObject().Where(p => p.Name != "$type").Select(p => (p.Name, p.Value)).ToList()
            : [];

    // A Newtonsoft set is either a plain array or { "$type": …, "$values": [ … ] }.
    private static IEnumerable<JsonElement> Values(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var v)) return [];
        if (v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().ToList();
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("$values", out var values) && values.ValueKind == JsonValueKind.Array)
            return values.EnumerateArray().ToList();
        return [];
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static DateTime? Date(JsonElement parent, string name) =>
        Text(parent, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.LocalDateTime : null;
}
