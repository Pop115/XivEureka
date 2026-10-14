using System;
using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Zones;

/// <summary>Une fenêtre de temps réel où une condition est remplie : en cours, ou à venir.</summary>
public sealed record Fenetre(DateTimeOffset Debut, DateTimeOffset Fin)
{
    public bool EnCours(DateTimeOffset maintenant) => Debut <= maintenant && maintenant < Fin;
}

/// <summary>
/// Où en est un NM maintenant : sa FATE si elle est en cours, quand il peut revenir s'il a été vu
/// tué dans cette instance, et quand sa condition et celle de sa prépa sont (ou seront) remplies.
/// </summary>
public sealed record SituationNm(NmEureka Nm, FateCarte? Fate, DateTimeOffset? RespawnAuPlusTot,
    Fenetre? FenetreNm, Fenetre? FenetrePrepa)
{
    public bool EnRespawn(DateTimeOffset maintenant) => Fate == null && RespawnAuPlusTot > maintenant;

    /// <summary>La première fenêtre à attendre (NM ou prépa) ; null si aucune condition.</summary>
    public Fenetre? Attente => FenetreNm ?? FenetrePrepa;
}

/// <summary>Le calcul des situations, sans rien du jeu : les FATE en cours et celles vues finir.</summary>
public static class Situations
{
    public static IReadOnlyList<SituationNm> Calculer(ZoneEureka zone, NmZone nms, IReadOnlyList<FateCarte> fates,
        IReadOnlyDictionary<ushort, DateTime> terminees, DateTimeOffset maintenant) =>
        nms.Nm.Select(nm =>
        {
            var fate = fates.FirstOrDefault(f => f.Id == nm.Fate);
            DateTimeOffset? respawn = terminees.TryGetValue(nm.Fate, out var fin)
                ? new DateTimeOffset(DateTime.SpecifyKind(fin, DateTimeKind.Utc)).AddMinutes(nms.RespawnMinutes)
                : null;
            return new SituationNm(nm, fate, respawn,
                ProchaineFenetre(zone, nm.Condition, maintenant), ProchaineFenetre(zone, nm.Prepa.Condition, maintenant));
        }).ToList();

    /// <summary>La fenêtre en cours ou la prochaine où la condition est remplie ; null sans condition.</summary>
    public static Fenetre? ProchaineFenetre(ZoneEureka zone, Condition condition, DateTimeOffset maintenant)
    {
        if (condition.Meteo is { } meteo)
        {
            // Plusieurs périodes de suite peuvent avoir la même météo : la fenêtre va jusqu'à la dernière.
            var periodes = Meteo.Periodes(zone, maintenant).TakeWhile(p => p.Debut < maintenant.AddDays(7))
                .SkipWhile(p => p.Meteo != meteo).TakeWhile(p => p.Meteo == meteo).ToList();
            return periodes.Count == 0 ? null : new Fenetre(periodes[0].Debut, periodes[^1].Fin);
        }
        if (condition.Nuit)
        {
            var (nuitApres, bascule) = Meteo.ProchaineBascule(maintenant);
            if (!nuitApres) return new Fenetre(maintenant, bascule); // il fait nuit jusqu'à l'aube
            var (_, aube) = Meteo.ProchaineBascule(bascule);
            return new Fenetre(bascule, aube);
        }
        return null;
    }
}
