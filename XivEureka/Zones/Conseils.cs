using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace XivEureka.Zones;

/// <summary>Une ligne de « Où aller maintenant » : quoi, pourquoi, et où sur la carte (coordonnées de carte).</summary>
public sealed record Conseil(int Priorite, string Titre, string Texte, Vector2 Coordonnees, uint? Pnj);

/// <summary>Les noms dans la langue du client, fournis par l'appelant (feuilles du jeu).</summary>
public sealed record NomsJeu(Func<uint, string> Pnj, Func<string, string> Meteo, Func<uint, string> Objet);

/// <summary>
/// « Où aller maintenant », sans rien du jeu : à partir des situations des NM, du niveau
/// élémentaire et des éclats Logos utiles, une courte liste classée. Règles, de la plus pressante à
/// la moins pressante : une FATE de NM en cours ; une condition (météo, nuit) qui s'ouvre dans la
/// demi-heure, avec la prépa à faire ; les NM disponibles à ton niveau ; les NM qui lâchent les
/// éclats qui te manquent.
/// </summary>
public static class Conseils
{
    public static readonly TimeSpan Horizon = TimeSpan.FromMinutes(30);

    public static IReadOnlyList<Conseil> Calculer(IReadOnlyList<SituationNm> situations, DateTimeOffset maintenant,
        int? niveau, IReadOnlyDictionary<uint, int> eclatsUtiles, NomsJeu noms, int max = 5)
    {
        var conseils = new List<Conseil>();

        foreach (var s in situations.Where(s => s.Fate != null))
        {
            var f = s.Fate!;
            var texte = f.EnPreparation ? "En préparation." : $"{f.Progres} %, encore {Formats.Duree(f.Restant)}.";
            conseils.Add(new Conseil(100 + f.Progres, $"{noms.Pnj(s.Nm.Pnj)} – FATE en cours", texte, s.Nm.Position, s.Nm.Pnj));
        }

        foreach (var s in situations.Where(s => s.Fate == null && !s.EnRespawn(maintenant)))
        {
            if (s.Attente is not { } fenetre || fenetre.Debut - maintenant > Horizon) continue;
            var condition = s.FenetreNm != null ? s.Nm.Condition : s.Nm.Prepa.Condition;
            var nomCondition = condition.Meteo is { } m ? noms.Meteo(m) : "Nuit";
            var quand = fenetre.EnCours(maintenant)
                ? $"{nomCondition} en cours (encore {Formats.Duree(fenetre.Fin - maintenant)})"
                : $"{nomCondition} dans {Formats.Duree(fenetre.Debut - maintenant)}";
            var minutes = (int)Math.Max(0, (fenetre.Debut - maintenant).TotalMinutes);
            conseils.Add(new Conseil(80 - minutes, $"{noms.Pnj(s.Nm.Pnj)} – {quand}", TextePrepa(s.Nm, noms),
                s.Nm.Prepa.Lieux.FirstOrDefault(s.Nm.Position), s.Nm.Pnj));
        }

        var disponibles = situations.Where(s => Disponible(s, maintenant)).ToList();

        if (niveau is { } n)
        {
            var aMonNiveau = disponibles.Where(s => s.Nm.Niveau >= n - 1 && s.Nm.Niveau <= n + 2)
                .OrderBy(s => Math.Abs(s.Nm.Niveau - n)).Take(3).ToList();
            if (aMonNiveau.Count > 0)
                conseils.Add(new Conseil(40, $"À ton niveau ({n})",
                    "Disponibles : " + string.Join(", ", aMonNiveau.Select(s => $"{noms.Pnj(s.Nm.Pnj)} (Nv {s.Nm.Niveau})")) + ".",
                    aMonNiveau[0].Nm.Position, aMonNiveau[0].Nm.Pnj));
        }

        foreach (var (eclat, actions) in eclatsUtiles.Where(e => e.Value > 0).OrderByDescending(e => e.Value))
        {
            var sources = disponibles.Where(s => s.Nm.Eclat == eclat).OrderBy(s => s.Nm.Niveau).Take(4).ToList();
            if (sources.Count == 0) continue;
            conseils.Add(new Conseil(30 + Math.Min(actions, 9), $"Logos – {noms.Objet(eclat)}",
                $"Sert à {actions} action(s) pas encore apprise(s). Disponibles : "
                + string.Join(", ", sources.Select(s => noms.Pnj(s.Nm.Pnj))) + ".",
                sources[0].Nm.Position, sources[0].Nm.Pnj));
        }

        return conseils.OrderByDescending(c => c.Priorite).Take(max).ToList();
    }

    /// <summary>Pas de FATE, pas en respawn, et sa condition (s'il en a une) est remplie maintenant.</summary>
    public static bool Disponible(SituationNm s, DateTimeOffset maintenant) =>
        s.Fate == null && !s.EnRespawn(maintenant) && (s.Attente == null || s.Attente.EnCours(maintenant));

    public static string TextePrepa(NmEureka nm, NomsJeu noms)
    {
        var lieux = string.Join(" ou ", nm.Prepa.Lieux.Select(Formats.Coordonnees));
        return $"Prépa : {noms.Pnj(nm.Prepa.Pnj)} (Nv {nm.Prepa.Niveau})" + (lieux.Length > 0 ? $" vers {lieux}." : ".");
    }
}

/// <summary>Mises en forme partagées par la carte et les conseils.</summary>
public static class Formats
{
    /// <summary>« 12 min », « 1 h 05 », « 40 s ».</summary>
    public static string Duree(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes:00}"
        : d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} min"
        : $"{Math.Max(0, (int)d.TotalSeconds)} s";

    /// <summary>« (16.8, 33.4) », comme sur la carte du jeu.</summary>
    public static string Coordonnees(Vector2 c) =>
        string.Create(CultureInfo.InvariantCulture, $"({c.X:0.0}, {c.Y:0.0})");
}
