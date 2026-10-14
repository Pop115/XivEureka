using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace XivEureka.Zones;

/// <summary>
/// Ce qui fait apparaître un NM ou son mob de prépa : une météo (nom anglais, clé des tranches de
/// <see cref="Meteo"/>) ou la nuit d'Eorzéa (aucun NM ne demande les deux). Sans l'un ni l'autre :
/// pas de condition.
/// </summary>
public sealed record Condition(string? Meteo, bool Nuit)
{
    public bool Aucune => Meteo == null && !Nuit;
}

/// <summary>Le mob à tuer pour faire apparaître le NM, son niveau, sa condition et où il vit (coordonnées de carte).</summary>
public sealed record Prepa(uint Pnj, int Niveau, Condition Condition, IReadOnlyList<Vector2> Lieux);

/// <summary>
/// Un NM d'Eurêka. <see cref="Pnj"/> : ligne BNpcName (nom dans la langue du client) ;
/// <see cref="Site"/> : son numéro sur ffxiv-eureka.com ; <see cref="Fate"/> : sa FATE ; <see cref="Position"/> : coordonnées de carte de la FATE ;
/// <see cref="Eclat"/> : le logogramme (éclat Logos non identifié) qu'il lâche ; <see cref="Objets"/> : ses objets rares.
/// </summary>
public sealed record NmEureka(uint Pnj, int Site, ushort Fate, int Niveau, string Element, Vector2 Position, uint Eclat,
    IReadOnlyList<uint> Objets, Condition Condition, Prepa Prepa);

/// <summary>Les NM d'une zone et leur délai de réapparition.</summary>
public sealed record NmZone(uint Territoire, int RespawnMinutes, IReadOnlyList<NmEureka> Nm);

/// <summary>Les NM connus, zone par zone, lus dans <c>Donnees/nm.json</c> (sources dans le fichier).</summary>
public sealed class CatalogueNm(IReadOnlyList<NmZone> zones)
{
    public NmZone? Zone(uint territoire) => zones.FirstOrDefault(z => z.Territoire == territoire);

    public static CatalogueNm Lire(Stream json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var brut = JsonSerializer.Deserialize<Fichier>(json, options) ?? throw new InvalidDataException("nm.json vide");
        return new CatalogueNm(brut.Zones.Select(z => new NmZone(z.Territoire, z.RespawnMinutes, z.Nm.Select(n => new NmEureka(
            n.Pnj, n.Site, n.Fate, n.Niveau, n.Element, new Vector2(n.X, n.Y), n.Eclat, n.Objets,
            new Condition(n.Meteo, n.Nuit),
            new Prepa(n.Prepa.Pnj, n.Prepa.Niveau, new Condition(n.Prepa.Meteo, n.Prepa.Nuit),
                n.Prepa.Lieux.Select(l => new Vector2(l[0], l[1])).ToList()))).ToList())).ToList());
    }

    public static CatalogueNm Integre()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("XivEureka.Donnees.nm.json")
            ?? throw new FileNotFoundException("Ressource nm.json absente de la DLL");
        return Lire(flux);
    }

    private sealed record Fichier(List<FichierZone> Zones);
    private sealed record FichierZone(uint Territoire, int RespawnMinutes, List<FichierNm> Nm);
    private sealed record FichierNm(uint Pnj, int Site, ushort Fate, int Niveau, string Element, float X, float Y, uint Eclat,
        List<uint> Objets, string? Meteo, bool Nuit, FichierPrepa Prepa);
    private sealed record FichierPrepa(uint Pnj, int Niveau, string? Meteo, bool Nuit, List<float[]> Lieux);
}
