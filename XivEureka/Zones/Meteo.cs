using System;
using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Zones;

/// <summary>Une zone d'Eurêka : sa ligne TerritoryType et ses tranches de météo (valeur 0-99 → météo).</summary>
public sealed record ZoneEureka(uint Territoire, string Nom, IReadOnlyList<(int Jusqua, string Meteo)> Tranches)
{
    public string MeteoPour(int valeur) => Tranches.First(t => valeur <= t.Jusqua).Meteo;
}

/// <summary>Une période de météo : de quand à quand (temps réel), et laquelle.</summary>
public sealed record PeriodeMeteo(DateTimeOffset Debut, DateTimeOffset Fin, string Meteo);

/// <summary>
/// Heure d'Eorzéa et météo d'Eurêka, calculées en local. Algorithme public, identique dans le JS de
/// ffxiv-eureka.com et dans EurekaHelper : une heure d'Eorzéa dure 175 s, une météo tient 8 heures
/// d'Eorzéa (1400 s). Les tranches sont celles de ffxiv-eureka.com (noms en anglais : ce sont aussi
/// les clés des conditions de NM).
/// </summary>
public static class Meteo
{
    public const int SecondesParHeure = 175;
    public const int SecondesParPeriode = 8 * SecondesParHeure;

    public static readonly IReadOnlyList<ZoneEureka> Zones =
    [
        new(732, "Anémos", [(29, "Fair Skies"), (59, "Gales"), (89, "Showers"), (99, "Snow")]),
        new(763, "Pagos", [(9, "Fair Skies"), (27, "Fog"), (45, "Heat Waves"), (63, "Snow"), (81, "Thunder"), (99, "Blizzards")]),
        new(795, "Pyros", [(9, "Fair Skies"), (27, "Heat Waves"), (45, "Thunder"), (63, "Blizzards"), (81, "Umbral Wind"), (99, "Snow")]),
        new(827, "Hydatos", [(11, "Fair Skies"), (33, "Showers"), (55, "Gloom"), (77, "Thunderstorms"), (99, "Snow")]),
    ];

    public static ZoneEureka? Zone(uint territoire) => Zones.FirstOrDefault(z => z.Territoire == territoire);

    /// <summary>Heure d'Eorzéa (0-23) et minute à l'instant donné.</summary>
    public static (int Heure, int Minute) HeureEorzea(DateTimeOffset instant)
    {
        var secondes = instant.ToUnixTimeSeconds();
        var minutes = secondes * 60 / SecondesParHeure;
        return ((int)(minutes / 60 % 24), (int)(minutes % 60));
    }

    /// <summary>Nuit d'Eorzéa au sens des NM : hors de 6 h - 19 h, comme sur ffxiv-eureka.com.</summary>
    public static bool Nuit(DateTimeOffset instant)
    {
        var (heure, _) = HeureEorzea(instant);
        return heure < 6 || heure >= 19;
    }

    /// <summary>Valeur 0-99 qui choisit la météo de la période contenant l'instant.</summary>
    public static int Valeur(DateTimeOffset instant)
    {
        var secondes = instant.ToUnixTimeSeconds();
        var heure = secondes / SecondesParHeure;
        var increment = (uint)((heure + 8 - (heure % 8)) % 24);
        var jours = (uint)(secondes / 4200);
        var base_ = jours * 100 + increment;
        var etape1 = (base_ << 11) ^ base_;
        var etape2 = (etape1 >> 8) ^ etape1;
        return (int)(etape2 % 100);
    }

    /// <summary>La période en cours puis les suivantes.</summary>
    public static IEnumerable<PeriodeMeteo> Periodes(ZoneEureka zone, DateTimeOffset depuis)
    {
        var debut = DateTimeOffset.FromUnixTimeSeconds(depuis.ToUnixTimeSeconds() / SecondesParPeriode * SecondesParPeriode);
        while (true)
        {
            var fin = debut.AddSeconds(SecondesParPeriode);
            yield return new PeriodeMeteo(debut, fin, zone.MeteoPour(Valeur(debut)));
            debut = fin;
        }
    }

    /// <summary>Prochaine période (en cours comprise) avec cette météo, cherchée sur une semaine réelle au plus.</summary>
    public static PeriodeMeteo? Prochaine(ZoneEureka zone, string meteo, DateTimeOffset depuis) =>
        Periodes(zone, depuis).TakeWhile(p => p.Debut < depuis.AddDays(7)).FirstOrDefault(p => p.Meteo == meteo);
}
