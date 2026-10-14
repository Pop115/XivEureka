using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace XivEureka.Tracker;

/// <summary>
/// Un message Phoenix Channels (protocole du serveur de ffxiv-eureka.com, version 2.0.0) :
/// <c>[join_ref, ref, sujet, évènement, charge]</c>.
/// </summary>
public sealed record MessagePhoenix(string? JoinRef, string? Ref, string Sujet, string Evenement, JsonElement Charge);

/// <summary>
/// Un tracker de ffxiv-eureka.com tel que le serveur le décrit : sa zone (numéros du site : 1 Anémos,
/// 2 Pagos, 3 Pyros, 4 Hydatos), son data center, l'heure de mort de chaque NM (numéros du site) et
/// les NM préparés. <see cref="MotDePasse"/> n'est connu que pour un tracker qu'on a créé.
/// </summary>
public sealed record InstanceTracker(string Id, int? Zone, int? DataCenter, IReadOnlyDictionary<int, DateTimeOffset> Morts,
    IReadOnlySet<int> Prepares, string? MotDePasse, DateTimeOffset? MajLe);

/// <summary>Lecture et écriture des messages du tracker, sans réseau (testable sur des relevés).</summary>
public static class FormatTracker
{
    /// <summary>Data centers du site (db/data-centers de ffxiv-eureka.com) : nom anglais du jeu → numéro.</summary>
    public static readonly IReadOnlyDictionary<string, int> DataCenters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Elemental"] = 1, ["Gaia"] = 2, ["Mana"] = 3, ["Aether"] = 4, ["Primal"] = 5, ["Chaos"] = 6,
        ["中国区"] = 7, ["中国二区"] = 8, ["한국"] = 9, ["Crystal"] = 10, ["Light"] = 11, ["Materia"] = 12,
        ["Dynamis"] = 13, ["Meteor"] = 14,
    };

    /// <summary>Zone du site pour un territoire d'Eurêka ; null ailleurs.</summary>
    public static int? ZoneSite(uint territoire) => territoire switch
    {
        732 => 1, 763 => 2, 795 => 3, 827 => 4, _ => null,
    };

    public static MessagePhoenix? Lire(string texte)
    {
        try
        {
            using var doc = JsonDocument.Parse(texte);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() != 5) return null;
            return new MessagePhoenix(Chaine(r[0]), Chaine(r[1]), r[2].GetString() ?? string.Empty,
                r[3].GetString() ?? string.Empty, r[4].Clone());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Ecrire(string? joinRef, string? @ref, string sujet, string evenement, object charge) =>
        JsonSerializer.Serialize(new object?[] { joinRef, @ref, sujet, evenement, charge });

    /// <summary>Un tracker complet (objet <c>data</c> de <c>initial_payload</c> ou de la réponse de création).</summary>
    public static InstanceTracker LireInstance(JsonElement data) =>
        Fusionner(new InstanceTracker(data.GetProperty("id").GetString() ?? string.Empty, ZoneDe(data), null,
            new Dictionary<int, DateTimeOffset>(), new HashSet<int>(), null, null), data);

    /// <summary>
    /// Applique une mise à jour (<c>payload</c>) : le serveur n'envoie que les attributs qui ont
    /// changé, les autres restent.
    /// </summary>
    public static InstanceTracker Fusionner(InstanceTracker avant, JsonElement data)
    {
        if (!data.TryGetProperty("attributes", out var a)) return avant;
        var apres = avant;
        if (a.TryGetProperty("notorious-monsters", out var morts)) apres = apres with { Morts = LireMorts(morts) };
        if (a.TryGetProperty("prepped-notorious-monsters", out var prep)) apres = apres with { Prepares = LirePrepares(prep) };
        if (a.TryGetProperty("data-center-id", out var dc)) apres = apres with { DataCenter = Entier(dc) };
        if (a.TryGetProperty("password", out var mdp) && mdp.ValueKind == JsonValueKind.String) apres = apres with { MotDePasse = mdp.GetString() };
        if (a.TryGetProperty("updated-at", out var maj) && maj.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(maj.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var quand))
            apres = apres with { MajLe = quand };
        return apres;
    }

    /// <summary>Corps JSON:API de la création d'un tracker (comme le site : <c>POST /api/instances</c>).</summary>
    public static string CorpsCreation(int zoneSite) =>
        JsonSerializer.Serialize(new { data = new { type = "instances", attributes = new Dictionary<string, object?> { ["copy-from"] = null, ["zone-id"] = zoneSite.ToString(CultureInfo.InvariantCulture) } } });

    private static int? ZoneDe(JsonElement data) =>
        data.TryGetProperty("relationships", out var rel) && rel.TryGetProperty("zone", out var z)
        && z.TryGetProperty("data", out var zd) && zd.ValueKind == JsonValueKind.Object && zd.TryGetProperty("id", out var id)
            ? Entier(id) : null;

    // « {"38":1792015799985} » : une chaîne JSON dans le JSON ; valeurs en millisecondes (nombre ou texte).
    private static Dictionary<int, DateTimeOffset> LireMorts(JsonElement brut)
    {
        var resultat = new Dictionary<int, DateTimeOffset>();
        if (Interieur(brut) is not { } doc) return resultat;
        using (doc)
            foreach (var p in doc.RootElement.EnumerateObject())
                if (int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nm) && Entier64(p.Value) is { } ms)
                    resultat[nm] = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return resultat;
    }

    private static HashSet<int> LirePrepares(JsonElement brut)
    {
        var resultat = new HashSet<int>();
        if (Interieur(brut) is not { } doc) return resultat;
        using (doc)
            foreach (var p in doc.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.True))
                if (int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nm)) resultat.Add(nm);
        return resultat;
    }

    private static JsonDocument? Interieur(JsonElement brut)
    {
        if (brut.ValueKind != JsonValueKind.String) return null;
        try
        {
            var doc = JsonDocument.Parse(brut.GetString() ?? "{}");
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
            doc.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Chaine(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static int? Entier(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetInt32(out var n) => n,
        JsonValueKind.String when int.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    private static long? Entier64(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetInt64(out var n) => n,
        JsonValueKind.String when long.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };
}
